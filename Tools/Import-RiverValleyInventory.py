"""从本机已下载的 Survival Clean 资源包恢复河谷镇背包素材。"""

import json
import os
import tarfile
from argparse import ArgumentParser
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[1]
PACKAGE = (
    Path(os.environ["APPDATA"])
    / "Unity/Asset Store-5.x/LAYERLAB/2DGUI/assetstore_package_20000443.unitypackage"
)
PACKAGE_ROOT = ROOT / "Client/Assets/ThirdParty/Layer Lab/GUI Pro-SurvivalClean"
INVENTORY_ROOT = PACKAGE_ROOT / "RiverValleyInventory"
LEGACY_ROOT = ROOT / "Client/Assets/Game/Art/RiverValleyInventory"
OUTPUT = INVENTORY_ROOT / "Resources/RiverValleyInventory"
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


def restore_inventory():
    """恢复游戏使用的原图副本，已有 Unity meta 由项目保留。"""
    if LEGACY_ROOT.is_dir():
        old_meta = LEGACY_ROOT.with_suffix(".meta")
        new_meta = INVENTORY_ROOT.with_suffix(".meta")
        if INVENTORY_ROOT.exists() or new_meta.exists():
            raise ValueError("新旧背包素材目录同时存在，需要先核对重复文件。")
        if not old_meta.is_file():
            raise ValueError(f"迁移所需的 Unity meta 文件缺失：{old_meta}")
        PACKAGE_ROOT.mkdir(parents=True, exist_ok=True)
        LEGACY_ROOT.rename(INVENTORY_ROOT)
        old_meta.rename(new_meta)
    with tarfile.open(PACKAGE) as package:
        restore_icons(package)


def restore_icons(package):
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


def import_complete_package():
    """还原原包的资源和 meta，将附带的演示脚本放在同一包内。"""
    files = {}
    with tarfile.open(PACKAGE) as package:
        for member in package:
            if member.name.endswith("/pathname"):
                key = member.name.partition("/")[0]
                path = PurePosixPath(package.extractfile(member).read().decode().strip())
                roots = ("Assets", "Layer Lab")
                if path.parts[:2] != roots or ".." in path.parts or len(path.parts) < 3:
                    raise ValueError(f"原包路径超出预期：{path}")
                folder = path.parts[2]
                if folder == "GUI Pro-SurvivalClean":
                    files[key] = PACKAGE_ROOT.joinpath(*path.parts[3:])
                elif folder == "Scripts":
                    files[key] = PACKAGE_ROOT.joinpath(*path.parts[2:])
                else:
                    raise ValueError(f"原包包含未登记目录：{path}")

    if not files or len(set(files.values())) != len(files):
        raise ValueError("原包清单为空或包含重复目标路径。")
    with tarfile.open(PACKAGE) as package:
        for member in package:
            key, _, entry = member.name.partition("/")
            if key not in files or entry not in ("asset", "asset.meta") or not member.isfile():
                continue
            destination = files[key]
            if entry == "asset.meta":
                destination = destination.with_name(destination.name + ".meta")
            data = package.extractfile(member).read()
            if destination.exists():
                if destination.read_bytes() == data:
                    continue
                raise ValueError(f"本地资源已有不同内容：{destination}")
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
    print(f"Survival Clean：已核对和导入 {len(files)} 条原包资源记录。")


if __name__ == "__main__":
    parser = ArgumentParser(description=__doc__)
    parser.add_argument("--full", action="store_true", help="同时导入完整演示及原包预制体")
    if parser.parse_args().full:
        import_complete_package()
    restore_inventory()
