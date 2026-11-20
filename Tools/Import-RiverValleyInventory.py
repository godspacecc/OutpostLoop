"""从本机已下载的 Survival Clean 资源包恢复河谷镇背包素材。"""

import json
import os
import tarfile
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[1]
PACKAGE = (
    Path(os.environ["APPDATA"])
    / "Unity/Asset Store-5.x/LAYERLAB/2DGUI/assetstore_package_20000443.unitypackage"
)
OUTPUT = ROOT / "Client/Assets/Game/Art/RiverValleyInventory/Resources/RiverValleyInventory"
SOURCE_RECORD = ROOT / "Backup/Artifacts/InventoryInGame-20261120/asset-sources.json"
ITEM_ICONS = {
    "Water": "Water", "Food": "Canfood", "Bandage": "Bandage", "Aid": "First-Aid",
    "Ammo": "Bullet_Pack", "Torch": "Flashlight",
    "Battery": "Battery", "Boards": "Wood_Panel", "Parts": "Parts", "Alarm": "Megaphone",
}
ASSETS = {
    name: f"noshadow_128/item_icon_{filename.lower()}.png"
    for name, filename in ITEM_ICONS.items()
}
ASSETS.update({
    "frame": "frame_custom/frame_frame05_white1.png",
    "button": "button_custom/btn_icontextbutton_square06_white1.png",
})


with tarfile.open(PACKAGE) as package:
    by_suffix = {suffix: name for name, suffix in ASSETS.items()}
    found = {}
    for member in package:
        if not member.name.endswith("/pathname"):
            continue
        pathname = package.extractfile(member).read().decode().strip()
        suffix = "/".join(PurePosixPath(pathname).parts[-2:]).lower()
        if suffix in by_suffix:
            found[by_suffix[suffix]] = (member.name.rsplit("/", 1)[0] + "/asset", pathname)
    if found.keys() != ASSETS.keys():
        raise ValueError(f"资源包缺少背包素材：{ASSETS.keys() - found.keys()}")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, (member_path, _) in found.items():
        (OUTPUT / f"{name}.png").write_bytes(package.extractfile(member_path).read())
    SOURCE_RECORD.parent.mkdir(parents=True, exist_ok=True)
    SOURCE_RECORD.write_text(
        json.dumps({name: source for name, (_, source) in sorted(found.items())}, indent=2) + "\n",
        encoding="utf-8",
    )
