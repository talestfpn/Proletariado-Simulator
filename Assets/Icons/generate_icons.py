"""Original vector illustrations for Proletariado Simulator. Run from any directory.

Each icon is drawn by hand below; no external assets, fonts or tracing are used.
The 96-unit canvas and four-unit strokes remain readable at 64 px.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parent
INK, PAPER, RED, TEAL, GOLD = "#28362F", "#F4E8C8", "#B94A38", "#287366", "#D5A344"

def path(d, fill="none", stroke=INK, width=4):
    return f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>'

def rect(x,y,w,h,fill,rx=2):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{INK}" stroke-width="4"/>'

def circle(x,y,r,fill):
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}" stroke="{INK}" stroke-width="4"/>'

def ellipse(x,y,rx,ry,fill):
    return f'<ellipse cx="{x}" cy="{y}" rx="{rx}" ry="{ry}" fill="{fill}" stroke="{INK}" stroke-width="4"/>'

icons = [
    # CORRE: coffee and a traffic light, bicycle, brickwork, register,
    # cooking pot, factory, clock-in card, cooperative roofs.
    [
        rect(17,39,39,31,RED,5)+path("M56 43h7a10 10 0 0 1 0 20h-7",GOLD)+path("M15 75h47M28 31c-9-9 8-9 0-18M43 31c-9-9 8-9 0-18")+rect(71,17,13,44,TEAL,5)+circle(77.5,27,2,GOLD)+circle(77.5,39,2,RED)+circle(77.5,51,2,GOLD)+path("M78 63v15"),
        circle(24,64,16,TEAL)+circle(73,64,16,TEAL)+path("M24 64l16-29 17 29H24l22-17h22l5 17M53 35l-3-10h12M35 34h13",PAPER)+rect(21,19,16,12,RED)+path("M73 43l-9-17h-7"),
        rect(12,61,27,18,RED)+rect(40,61,27,18,GOLD)+rect(26,42,27,18,RED)+rect(54,42,27,18,RED)+path("M18 33L43 13l15 17-31 8z",TEAL)+path("M44 28l10 10M55 38l13-11",PAPER,INK,7),
        rect(15,57,67,22,TEAL)+path("M18 55l7-26h47l8 26z",GOLD)+rect(34,12,30,20,PAPER)+path("M41 19h16M41 25h10M27 41h6m9 0h6m9 0h6M26 48h7m9 0h6m9 0h6M44 67h12")+circle(24,67,3,RED)+path("M34 78v5m31-5v5"),
        path("M21 46h55v14a23 23 0 0 1-23 22h-9a23 23 0 0 1-23-22z",TEAL)+path("M15 46h67M20 54h-9v12h11M77 54h9v12H75M29 37c-11-10 11-11 0-22M47 37c-11-10 11-11 0-22M65 37c-11-10 11-11 0-22")+path("M31 70h22",PAPER,PAPER),
        path("M13 80V46l21 10V38l22 13V17h15v39l11 6v18z",RED)+path("M56 29h15M20 67h8m12 0h8m14 0h8M21 76h7m12 0h8m14 0h8",PAPER)+path("M64 11c-9-5 7-6 2-9",PAPER,TEAL)+path("M39 80V66h12v14",GOLD),
        rect(16,21,63,60,TEAL,5)+rect(25,11,26,18,PAPER)+path("M31 18h14")+circle(48,48,19,PAPER)+path("M48 35v14l10 5M28 74h13m17 0h10")+path("M67 12l7 7 13-15", "none", RED),
        path("M12 49l19-17 19 17M49 49l18-17 18 17",RED)+rect(17,50,28,24,GOLD)+rect(54,50,26,24,TEAL)+path("M27 73V60h8v13M63 73V60h8v13")+path("M20 28l11-9 13 9M55 28l12-9 13 9")+path("M15 83h67M43 25h11M49 20v11",PAPER),
    ],
    # SINDICATO: collection tin, leaflets, meeting, strike fund,
    # assembly podium, sound truck, negotiating table, union banner.
    [
        ellipse(47,39,27,14,RED)+path("M20 39v27c0 18 54 18 54 0V39",RED)+ellipse(47,39,27,14,GOLD)+path("M36 38h22")+circle(47,16,11,TEAL)+path("M47 9v14m-4-10h6m-6 7h6",PAPER,PAPER,3)+path("M80 39l6-8M10 37l-6-7"),
        path("M23 27L67 15l15 57-44 12z",GOLD)+path("M15 15h43l13 59H28z",PAPER)+path("M26 29h24M29 39h25M31 48h18M33 58h24",PAPER)+path("M58 18l9-3 4 17",RED)+path("M16 68l7 10M10 76l6 8",TEAL,TEAL),
        ellipse(48,55,25,15,GOLD)+circle(48,17,9,RED)+circle(17,44,8,TEAL)+circle(79,44,8,TEAL)+circle(31,78,8,RED)+circle(65,78,8,RED)+path("M34 32q14-10 28 0M11 56v13M85 56v13M20 89h20M55 89h20"),
        rect(14,35,68,45,RED,5)+path("M14 46h68M24 35V23h48v12",GOLD)+rect(38,46,20,24,GOLD)+circle(48,55,3,INK)+path("M48 57v6M21 71h10m34 0h10")+path("M39 17l9-10 9 10",TEAL),
        path("M36 40h29v37H36z",RED)+path("M31 40h39M49 39V25l12-7M60 18l6 1",TEAL)+circle(49,15,9,TEAL)+path("M34 31q14-13 29 0")+circle(16,61,6,GOLD)+circle(81,61,6,GOLD)+path("M7 79q9-18 19 0M71 79q10-18 18 0M36 81h29"),
        rect(10,44,50,29,TEAL)+path("M60 49h16l10 13v11H60z",GOLD)+circle(27,75,9,INK)+circle(72,75,9,INK)+rect(19,18,32,25,RED)+path("M25 31h10l10-9v18l-10-9M63 20l11-8M66 30h16M63 39l11 8",PAPER,PAPER,3)+rect(66,53,9,10,PAPER),
        path("M14 55h68v12H14z",GOLD)+path("M21 67v17M75 67v17M34 55v-8m28 8v-8")+circle(22,24,9,TEAL)+circle(74,24,9,RED)+path("M10 47V38l12-6 19 16M86 47V38l-12-6-19 16",TEAL)+path("M34 41l13-5 15 9-9 10-11-8-6 3z",PAPER)+path("M41 18h14M48 11v14",TEAL),
        path("M19 83V14M20 17h57l-9 14 9 14H20",RED)+path("M34 38V26l8 8 9-11 7 15",GOLD,GOLD)+path("M28 58h43M34 67h32M41 76h17",TEAL)+path("M10 83h20M48 45v9"),
    ],
    # BRASÍLIA: campaign leaflet, canvasser, committee, rally microphone,
    # electoral stage, office desk, coalition, national broadcast.
    [
        path("M22 10h45l9 12v61H22z",PAPER)+path("M65 10v15h11",GOLD)+circle(48,33,10,TEAL)+path("M33 55q15-22 30 0",RED)+path("M33 65h30M35 74h26")+path("M11 34l7 5M82 40l7-6"),
        circle(36,25,12,GOLD)+path("M16 72V49q20-17 40 0v23z",TEAL)+path("M31 44l5 13 7-13",PAPER)+rect(54,37,28,42,PAPER)+rect(61,32,14,10,RED)+path("M61 51h13M61 60h13M61 69h7M22 73v10m24-10v10"),
        path("M10 42L48 13l38 29",RED)+rect(18,41,60,40,GOLD)+rect(41,53,17,28,TEAL)+rect(24,51,11,13,PAPER)+rect(64,51,8,13,PAPER)+path("M31 35h35",PAPER,PAPER)+path("M47 13V6h21l-7 7h-14",RED)+circle(54,65,1,GOLD),
        path("M39 25v21a10 10 0 0 0 20 0V25a10 10 0 0 0-20 0z",RED)+path("M32 41v5a17 17 0 0 0 34 0v-5M49 65v17M37 82h24M44 23h10M44 30h10M44 37h10")+path("M16 26l8 6M13 42h10M73 26l8-6M74 42h12",GOLD,GOLD),
        rect(13,66,70,15,TEAL)+path("M17 66V18h63v48M17 23h63M17 18l18 23 13-11 15 11 17-23",RED)+rect(37,42,24,24,GOLD)+path("M32 43h34M47 42V29l9-3M13 84h71M21 81v6m52-6v6")+path("M8 17h14M75 17h13",GOLD),
        rect(11,49,73,15,RED)+path("M18 64v19M77 64v19M53 49V28l-12-9M32 25l11-10 10 15z",GOLD)+rect(20,29,21,20,TEAL)+path("M24 34h13M28 39h5M60 70h14M60 78h14M57 64v18")+path("M67 41h11v8H67",PAPER),
        circle(25,24,11,TEAL)+circle(73,24,11,RED)+circle(49,72,11,GOLD)+path("M36 22h26M80 35l-19 26M17 35l19 26",PAPER)+path("M18 23l6 5 8-10M66 23l6 5 8-10M42 71l6 5 8-10",PAPER,PAPER,3)+path("M37 45l12-8 13 8-12 11z",PAPER),
        path("M31 82l17-56 17 56M38 63h20M33 79h29M43 44h10",TEAL)+circle(48,22,7,RED)+path("M32 15q-10 9 0 18M64 15q10 9 0 18M22 8q-18 17 0 34M74 8q18 17 0 34",PAPER,INK,4)+circle(17,66,7,GOLD)+circle(79,66,7,GOLD)+path("M25 62l11-8M71 62l-11-8",TEAL),
    ],
]

for world_index, world in enumerate(icons, 1):
    directory = ROOT / f"world-{world_index}"
    directory.mkdir(parents=True, exist_ok=True)
    for producer_index, body in enumerate(world, 1):
        svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96">
<title>Proletariado Simulator — world-{world_index}/producer-{producer_index}</title>
<circle cx="49" cy="50" r="44" fill="#28362F" opacity=".14"/>
<circle cx="47" cy="46" r="44" fill="{PAPER}"/>
{body}
</svg>
'''
        (directory / f"producer-{producer_index}.svg").write_text(svg, encoding="utf-8")
print("24 original activity icons generated.")
