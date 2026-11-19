# Омск в LIFE CRAFT RP 2.0

Карта использует входящий в проект снимок OpenStreetMap от 17 ноября 2026 года. Координаты зданий, улиц, воды и территории парка взяты из снимка. Внешний вид достопримечательностей воссоздан в 3D по публичным фотографиям. Входные галереи торговых центров адаптированы для игры. [OpenStreetMap · © участники, ODbL](https://www.openstreetmap.org/copyright)

- ТЦ «Парк», улица Масленникова, 134. Контур `way/814296045`. [OpenStreetMap](https://www.openstreetmap.org/way/814296045) · [Администрация Омска](https://admomsk.ru/web/guest/progress/invest/climate/malls) · [Яндекс Карты](https://yandex.com/maps/org/park/243416676175/)
- ТЦ «МЕГА», бульвар Архитекторов, 35. Контур `way/61367812`. [OpenStreetMap](https://www.openstreetmap.org/way/61367812) · [МЕГА](https://fortune.mega.ru/) · [Фотографии фасада](https://omsk.rbc.ru/omsk/04/08/2025/6890a54e9a794710e661a875)
- Парк культуры и отдыха имени 30-летия ВЛКСМ, улица Масленникова, 136. Территория `relation/2148145`. [OpenStreetMap](https://www.openstreetmap.org/relation/2148145) · [Официальный сайт](https://park-omsk.ru/) · [2ГИС](https://go.2gis.com/9uca4) · [Западный вход](https://www.bk55.ru/news/article/247487/print/)

В игре откройте карту клавишей **M** или экранной кнопкой. Выберите место в верхнем ряду. Кнопка **Посетить** перемещает к входу в одиночной игре. Карта и сами локации доступны и в сетевой игре при передвижении по городу.

## Сборка картографических ресурсов

Команды выполняются из папки `LifeCraftRP`. Для картинок требуется Python с Pillow. Для мешей используется Python из Blender.

```sh
python Tools/build_omsk_map.py --landmark-maps
python Tools/build_omsk_landmarks.py
python Tools/build_omsk_park.py SourceData/omsk.geojson.gz Assets/Resources/Omsk/ParkLayout.json
blender -b --python-exit-code 1 -P Tools/build_omsk.py -- SourceData/omsk.geojson.gz Assets/Resources/Omsk --tile=-14_0 --tile=3_0 --exclude-building-ids way/61367812 way/814296045
blender -b --python-exit-code 1 -P Tools/build_omsk_detail.py -- SourceData/omsk.geojson.gz Assets/Resources/Omsk/Detail
```
