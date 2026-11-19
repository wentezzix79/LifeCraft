#!/usr/bin/env python3
"""Draw original sign textures with Pillow and the DejaVu typefaces."""

from os import getenv
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

DEST = Path(__file__).resolve().parents[1] / "Assets/Resources/Art/OmskSigns"
FONTS = Path(getenv("LIFECRAFT_DEJAVU_FONT_DIR", "/usr/share/fonts/truetype/dejavu"))
SCALE = 2


def canvas(size):
    return Image.new("RGBA", tuple(d * SCALE for d in size))


def polygon(image, points, color):
    ImageDraw.Draw(image).polygon([(round(x*SCALE),round(y*SCALE)) for x,y in points],fill=color)


def ellipse(image, bounds, color):
    ImageDraw.Draw(image).ellipse(tuple(round(x*SCALE) for x in bounds),fill=color)


def stroke(image, points, size, color):
    draw = ImageDraw.Draw(image)
    draw.line([(round(x*SCALE),round(y*SCALE)) for x,y in points],
              fill=color,width=round(size*SCALE),joint="curve")
    rad = size/2
    for x,y in points:
        ellipse(image,(x-rad,y-rad,x+rad,y+rad),color)


def glyph(image, letter, color, bounds, *, slant=0, font="DejaVuSans-Bold.ttf"):
    typeface = ImageFont.truetype(str(FONTS / font),420)
    left,top,right,bottom = typeface.getbbox(letter)
    text = Image.new("RGBA", (right-left+12,bottom-top+12))
    ImageDraw.Draw(text).text((6-left,6-top),letter,fill=color,font=typeface)
    text = text.crop(text.getbbox())
    if slant:
        text = text.rotate(slant,Image.Resampling.BICUBIC,expand=True)
        text = text.crop(text.getbbox())
    x0,y0,x1,y1 = bounds
    text = text.resize((round((x1-x0)*SCALE),round((y1-y0)*SCALE)),Image.Resampling.LANCZOS)
    image.alpha_composite(text,(round(x0*SCALE),round(y0*SCALE)))


def mega(aerial=False):
    image = canvas((1000,302))
    # Different colourways are visible on the entrance and the parking pylon.
    polygon(image,[(14,15),(30,5),(191,5),(212,25),(226,25),(235,10),(272,14),
                   (263,254),(245,283),(15,283),(9,272)],"#138849")
    polygon(image,[(276,14),(470,20),(490,31),(474,280),(272,284),(255,265),(267,38)],"#eab914")
    polygon(image,[(462,26),(495,13),(692,11),(730,28),(720,98),(714,272),
                   (684,289),(483,288),(454,266),(450,85)],"#dc931d" if aerial else "#a82128")
    polygon(image,[(747,20),(905,20),(924,30),(992,265),(990,284),(976,291),
                   (718,288),(701,274),(722,99)],"#13538f")
    stroke(image,[(66,220),(66,68),(135,150),(199,68),(197,220)],42,"#b1252b")
    glyph(image,"e","#14528e",(280,94,489,279),slant=21,font="DejaVuSans-BoldOblique.ttf")
    polygon(image,[(522,263),(570,264),(586,100),(689,99),(697,54),
                   (541,54),(526,68)],"#227346" if aerial else "#eee5cb")
    glyph(image,"a","#f9ce41",(750,61,940,258),slant=-3,font="DejaVuSerif-BoldItalic.ttf")
    return image


def park():
    image = canvas((1200,400))
    orange = "#dc492e"
    # Anniversary-style circular tree symbol mounted to the left of the word.
    ellipse(image,(9,20,346,357),orange)
    mask = canvas((1200,400))
    stroke(mask,[(136,359),(157,277),(188,225),(193,173)],24,"#ffffff")
    stroke(mask,[(176,241),(148,216),(126,208)],18,"#ffffff")
    stroke(mask,[(188,219),(220,193),(251,191)],18,"#ffffff")
    ellipse(mask,(77,148,153,219),"#ffffff")
    ellipse(mask,(144,103,227,181),"#ffffff")
    ellipse(mask,(219,141,305,220),"#ffffff")
    ellipse(mask,(155,180,229,240),"#ffffff")
    alpha = image.getchannel("A")
    alpha.paste(0,(0,0),mask.getchannel("A"))
    image.putalpha(alpha)
    glyph(image,"п",orange,(356,125,560,330))
    glyph(image,"а",orange,(562,120,765,334))
    glyph(image,"р",orange,(775,120,974,386))
    glyph(image,"к",orange,(984,125,1187,331))
    return image


def wing(auchan):
    image = canvas((1000,228))
    if auchan:
        glyph(image,"Ашан","#b91e2a",(9,14,989,220))
        polygon(image,[(105,118),(138,102),(177,111),(208,91),(181,134),(143,134),
                       (130,148),(110,145)],"#44a44b")
    else:
        glyph(image,"М.Видео","#b8222a",(8,8,993,220),font="DejaVuSerif-BoldItalic.ttf")
    return image


def save(name,image):
    image.resize((image.width//SCALE,image.height//SCALE),Image.Resampling.LANCZOS).save(
        DEST / (name + ".png"), optimize=True)


if __name__ == "__main__":
    DEST.mkdir(parents=True,exist_ok=True)
    save("MegaEntrance",mega())
    save("MegaPylon",mega(aerial=True))
    save("ParkRoof",park())
    save("MegaAuchan",wing(True))
    save("MegaVideo",wing(False))
