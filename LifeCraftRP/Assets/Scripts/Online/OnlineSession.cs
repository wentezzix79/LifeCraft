using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LifeCraft.World;
using UnityEngine;
using UnityEngine.Networking;

namespace LifeCraftRP
{
    [Serializable]
    public sealed class OnlineChatMessage
    {
        public int id;
        public string name;
        public string message;
    }

    [DisallowMultipleComponent]
    public sealed class OnlineSession : MonoBehaviour
    {
        public static OnlineSession Instance { get; private set; }
        public bool IsOnline { get; private set; }
        public bool IsConnecting { get; private set; }
        public int Cash { get; private set; }
        public int WorkStage { get; private set; }
        public int WorkShelf { get; private set; } = -1;
        public string UserName { get; private set; } = "";
        public string Status { get; private set; } = "Автономный режим";
        public OnlineChatMessage[] RecentChat { get; private set; } = Array.Empty<OnlineChatMessage>();
        public string BusId { get; private set; } = "";
        public int BusSeat { get; private set; }
        public bool BusExitPending { get; private set; }
        public bool BusCommandPending => pendingBus != null;
        public bool SupportsBuses => busClock > 0;
        public double BusTime => SupportsBuses
            ? busClock + Time.realtimeSinceStartupAsDouble - busClockReceived
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<int> CashChanged;
        public event Action<string> StatusChanged;
        public event Action ChatChanged;
        internal event Action<Vector3, Quaternion> BusExited;

        [Serializable]
        private sealed class PlayerState
        {
            public string name;
            public float x, y, z, yaw;
            public string busId;
            public int busSeat;
        }

        [Serializable]
        private sealed class RequestBody
        {
            public string name, password, message, action;
            public string busId, requestId;
            public float x, y, z, yaw;
            public bool resync;
        }

        [Serializable]
        private sealed class Reply
        {
            public string message, token;
            public int cash, stage, shelf;
            public bool corrected;
            public string busId;
            public int busSeat;
            public double busTime;
            public bool busExitPending;
            public PlayerState self;
            public PlayerState[] peers;
            public OnlineChatMessage[] chat;
        }

        private sealed class RemotePlayer
        {
            public GameObject root;
            public TextMesh label;
            public PlayerAnimationController appearance;
            public Vector3 position;
            public Quaternion rotation;
            public string busId;
            public int busSeat;
        }

        private readonly Dictionary<string, RemotePlayer> peers = new Dictionary<string, RemotePlayer>();
        private readonly HashSet<string> seenPeers = new HashSet<string>();
        private readonly List<string> expiredPeers = new List<string>();
        private Transform avatar;
        private OmskBusService buses;
        private string endpoint;
        private string token;
        private bool positionResync;
        private string pendingWork;
        private string pendingChat;
        private RequestBody pendingBus;
        private int busAttempts;
        private double busClock, busClockReceived;
        private UnityWebRequest activeRequest;
        private int operationVersion;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        public void Configure(Transform avatarTransform, OmskBusService busService)
        {
            avatar = avatarTransform;
            buses = busService;
        }

        public void LogIn(string host, string user, string password) => Authenticate(host, user, password, "/login");

        public void Register(string host, string user, string password) => Authenticate(host, user, password, "/register");

        private void Authenticate(string host, string user, string password, string route)
        {
            if (IsOnline || IsConnecting)
                return;

            host = host?.Trim() ?? "";
            if (!host.Contains("://"))
                host = "http://" + host;
            if (!Uri.TryCreate(host, UriKind.Absolute, out var address)
                || (address.Scheme != "http" && address.Scheme != "https")
                || address.AbsolutePath != "/" || address.Query.Length > 0
                || address.Fragment.Length > 0 || address.UserInfo.Length > 0 || address.Host.Length == 0)
            {
                SetStatus("Введите адрес сервера, например http://127.0.0.1:8080.");
                return;
            }
            if (avatar == null)
            {
                SetStatus("Персонаж ещё не создан.");
                return;
            }

            endpoint = address.GetLeftPart(UriPartial.Authority);
            IsConnecting = true;
            SetStatus("Подключение…");
            StartCoroutine(Connect(route, new RequestBody { name = user?.Trim(), password = password }, ++operationVersion));
        }

        private IEnumerator Connect(string route, RequestBody body, int version)
        {
            Reply response = null;
            yield return Request(endpoint, route, body, null, (reply, success, _) =>
            {
                if (version != operationVersion)
                    return;
                if (success)
                    response = reply;
                else
                    SetStatus(reply.message);
            });
            body.password = null;
            if (version != operationVersion)
                yield break;

            IsConnecting = false;
            if (response == null)
                yield break;
            if (string.IsNullOrEmpty(response.token) || response.self == null)
            {
                SetStatus("Сервер прислал неполные данные входа.");
                yield break;
            }

            token = response.token;
            UserName = response.self.name;
            IsOnline = true;
            ApplyPresence(response,true);
            ApplyEconomy(response);
            SetStatus(response.message);
            Connected?.Invoke();
            if (version == operationVersion && IsOnline)
                yield return Poll(version);
        }

        public void SendChat(string message)
        {
            if (!IsOnline || pendingChat != null)
                return;
            message = message?.Trim();
            if (!string.IsNullOrEmpty(message) && message.Length <= 200)
                pendingChat = message;
        }

        public void TryWork(string action)
        {
            if (IsOnline && pendingWork == null && !string.IsNullOrEmpty(action))
                pendingWork = action;
        }

        public void ResyncPosition() => positionResync = true;

        internal void SendBusCommand(string action, string id)
        {
            if (!IsOnline || !SupportsBuses || pendingBus != null || string.IsNullOrEmpty(id))
                return;
            pendingBus = new RequestBody { action = action, busId = id, requestId = Guid.NewGuid().ToString("N") };
            busAttempts = 0;
        }

        public void LogOut()
        {
            if (!IsOnline && !IsConnecting)
                return;
            string sessionToken = token;
            string sessionEndpoint = endpoint;
            EndSession("Автономный режим");
            if (!string.IsNullOrEmpty(sessionToken))
                StartCoroutine(Request(sessionEndpoint, "/logout", new RequestBody(), sessionToken, null, false));
        }

        private IEnumerator Poll(int version)
        {
            int failedPolls = 0;
            while (IsOnline && version == operationVersion)
            {
                if (avatar == null)
                {
                    LogOut();
                    yield break;
                }
                Vector3 position = avatar.position;
                var body = new RequestBody
                {
                    x = position.x, y = position.y, z = position.z,
                    yaw = avatar.eulerAngles.y, resync = positionResync
                };
                bool syncSucceeded = false;
                yield return Request(endpoint, "/sync", body, token, (reply, success, code) =>
                {
                    if (version != operationVersion)
                        return;
                    if (code == 401)
                    {
                        EndSession(reply.message);
                        return;
                    }
                    if (!success)
                    {
                        SetStatus(reply.message);
                        return;
                    }
                    syncSucceeded = true;
                    ApplyPresence(reply,reply.corrected);
                    positionResync = false;
                    ApplyEconomy(reply);
                    ApplyPeers(reply.peers);
                    ApplyChat(reply.chat);
                });
                if (version != operationVersion)
                    yield break;

                if (syncSucceeded)
                {
                    if (failedPolls > 0)
                        SetStatus("Связь с сервером восстановлена.");
                    failedPolls = 0;
                    if (pendingBus != null)
                        yield return BusCommand(version);
                    if (version == operationVersion && pendingWork != null)
                    {
                        string action = pendingWork;
                        pendingWork = null;
                        yield return Command("/work", new RequestBody { action = action }, version);
                    }
                    if (version == operationVersion && pendingChat != null)
                    {
                        string message = pendingChat;
                        pendingChat = null;
                        yield return Command("/chat", new RequestBody { message = message }, version);
                    }
                }
                else if (++failedPolls >= 4)
                {
                    EndSession("Сервер недоступен. Автономный режим.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(syncSucceeded ? .22f : 1f);
            }
        }

        private IEnumerator BusCommand(int version)
        {
            RequestBody body = pendingBus;
            yield return Request(endpoint, "/bus", body, token, (reply, success, code) =>
            {
                if (version != operationVersion)
                    return;
                if (code == 401)
                {
                    EndSession(reply.message);
                    return;
                }
                if (success)
                {
                    ApplyPresence(reply,reply.corrected);
                    ApplyEconomy(reply);
                }
                else if ((code == 0 || code >= 500) && ++busAttempts < 3)
                {
                    return;
                }
                pendingBus = null;
                SetStatus(reply.message);
            });
        }

        private IEnumerator Command(string route, RequestBody body, int version)
        {
            yield return Request(endpoint, route, body, token, (reply, success, code) =>
            {
                if (version != operationVersion)
                    return;
                if (code == 401)
                {
                    EndSession(reply.message);
                    return;
                }
                if (route == "/work" && success)
                    ApplyEconomy(reply);
                if (route == "/work" || !success)
                    SetStatus(reply.message);
            });
        }

        private IEnumerator Request(string host, string route, RequestBody body, string sessionToken,
            Action<Reply, bool, long> complete, bool tracked = true)
        {
            using var request = new UnityWebRequest(host + route, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 5;
            request.redirectLimit = 0;
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(sessionToken))
                request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
            if (tracked)
                activeRequest = request;

            UnityWebRequestAsyncOperation operation = null;
            string failure = null;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (InvalidOperationException ex)
            {
                failure = ex.Message;
            }
            if (operation != null)
                yield return operation;
            if (ReferenceEquals(activeRequest, request))
                activeRequest = null;

            Reply data = null;
            try
            {
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
                    data = JsonUtility.FromJson<Reply>(request.downloadHandler.text);
            }
            catch (ArgumentException)
            {
                data = null;
            }

            bool success = operation != null && request.result == UnityWebRequest.Result.Success && data != null;
            if (!success)
            {
                data ??= new Reply();
                if (string.IsNullOrEmpty(data.message))
                    data.message = "Не удалось связаться с сервером. " + (failure ?? request.error);
            }
            complete?.Invoke(data, success, request.responseCode);
        }

        private void ApplyEconomy(Reply response)
        {
            WorkStage = response.stage;
            WorkShelf = response.shelf;
            if (Cash != response.cash)
            {
                Cash = response.cash;
                CashChanged?.Invoke(Cash);
            }
        }

        private void ApplyLocation(PlayerState state)
        {
            if (avatar == null)
                return;
            var controller = avatar.GetComponent<CharacterController>();
            bool restoreController = controller != null && controller.enabled;
            if (restoreController)
                controller.enabled = false;
            avatar.SetPositionAndRotation(new Vector3(state.x, state.y, state.z), Quaternion.Euler(0, state.yaw, 0));
            if (restoreController)
                controller.enabled = true;
        }

        private void ApplyPresence(Reply reply, bool applyLocation)
        {
            string previous = BusId;
            if (reply.busTime > 0 && !double.IsInfinity(reply.busTime))
            {
                double estimate = BusTime;
                busClock = SupportsBuses && Math.Abs(estimate-reply.busTime) < .45
                    ? estimate + (reply.busTime - estimate) * .15
                    : reply.busTime;
                busClockReceived = Time.realtimeSinceStartupAsDouble;
                BusId = reply.busId ?? "";
                BusSeat = reply.busSeat;
                BusExitPending = reply.busExitPending;
            }
            if (reply.self == null || BusId.Length > 0)
                return;
            if (previous.Length > 0 && BusExited != null)
            {
                PlayerState state = reply.self;
                BusExited.Invoke(new Vector3(state.x,state.y,state.z),Quaternion.Euler(0,state.yaw,0));
            }
            else if (applyLocation)
                ApplyLocation(reply.self);
        }

        private void ApplyChat(OnlineChatMessage[] items)
        {
            if (items == null || (items.Length == RecentChat.Length
                && (items.Length == 0 || items[items.Length - 1].id == RecentChat[RecentChat.Length - 1].id)))
                return;

            RecentChat = items;
            ChatChanged?.Invoke();
        }

        private void ApplyPeers(PlayerState[] players)
        {
            seenPeers.Clear();
            foreach (var state in players ?? Array.Empty<PlayerState>())
            {
                if (state == null || string.IsNullOrEmpty(state.name))
                    continue;
                seenPeers.Add(state.name);
                Vector3 position = new Vector3(state.x, state.y, state.z);
                if (!peers.TryGetValue(state.name, out var peer))
                {
                    peer = CreatePeer(state.name, position);
                    peers.Add(state.name, peer);
                }
                peer.position = position;
                peer.rotation = Quaternion.Euler(0, state.yaw, 0);
                peer.busId = state.busId;
                peer.busSeat = state.busSeat;
            }

            expiredPeers.Clear();
            foreach (var entry in peers)
            {
                if (!seenPeers.Contains(entry.Key))
                {
                    Destroy(entry.Value.root);
                    expiredPeers.Add(entry.Key);
                }
            }
            foreach (string key in expiredPeers)
                peers.Remove(key);
        }

        private RemotePlayer CreatePeer(string playerName, Vector3 position)
        {
            var root = new GameObject("Сетевой игрок " + playerName);
            root.transform.SetParent(transform);
            root.transform.position = position;
            var appearance = root.AddComponent<PlayerAnimationController>();
            appearance.Build();

            var nameplate = new GameObject("Имя игрока");
            nameplate.transform.SetParent(root.transform, false);
            nameplate.transform.localPosition = Vector3.up * 2.08f;
            var label = nameplate.AddComponent<TextMesh>();
            label.text = playerName;
            label.anchor = TextAnchor.MiddleCenter;
            label.fontSize = 48;
            label.characterSize = .083f;
            label.color = new Color(.70f, .93f, 1f);

            return new RemotePlayer { root = root, label = label, appearance = appearance,
                position = position, rotation = Quaternion.identity };
        }

        private void Update()
        {
            Camera mainCamera = Camera.main;
            float weight = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            foreach (var peer in peers.Values)
            {
                var pose = peer.root.transform;
                bool riding = !string.IsNullOrEmpty(peer.busId);
                float speed = 0f;
                if (riding && buses != null && buses.TryGetBus(peer.busId,out var bus) && bus.Active)
                {
                    pose.SetPositionAndRotation(bus.GetSeatPosition(peer.busSeat),bus.Rotation);
                }
                else
                {
                    speed = Mathf.Min(Vector3.Distance(pose.position,peer.position)*14f,7f);
                    pose.SetPositionAndRotation(Vector3.Lerp(pose.position,peer.position,weight),
                        Quaternion.Slerp(pose.rotation,peer.rotation,weight));
                }
                peer.appearance.Animate(speed,true,false,false,0,riding);
                if (mainCamera != null)
                    peer.label.transform.rotation = mainCamera.transform.rotation;
            }
        }

        private void SetStatus(string value)
        {
            Status = value;
            StatusChanged?.Invoke(value);
        }

        private void EndSession(string message)
        {
            bool wasConnected = IsOnline;
            ++operationVersion;
            activeRequest?.Abort();
            activeRequest = null;
            IsOnline = IsConnecting = false;
            token = null;
            Cash = WorkStage = 0;
            WorkShelf = -1;
            UserName = "";
            positionResync = false;
            pendingWork = pendingChat = null;
            pendingBus = null;
            BusId = "";
            BusSeat = 0;
            BusExitPending = false;
            busClock = 0;
            ApplyPeers(null);
            RecentChat = Array.Empty<OnlineChatMessage>();
            ChatChanged?.Invoke();
            SetStatus(message);
            if (wasConnected)
                Disconnected?.Invoke();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            activeRequest?.Abort();
        }
    }
}
