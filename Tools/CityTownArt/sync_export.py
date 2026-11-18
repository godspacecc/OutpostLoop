from pathlib import Path
import re,shutil
# 将独立项目烘焙结果同步到本项目自己的目录；不搬运原包脚本、设置或其他项目内容。
source=Path('Backup/Artifacts/PlayerMotion-20261118/PreviewProject/Assets/CityTownArtExport')
target=Path('Client/Assets/Art/CityTown')
if __name__=='__main__':
 if not source.is_dir():raise SystemExit('请先在独立预览项目运行 CityTownArtBuilder.Run')
 target.mkdir(parents=True,exist_ok=True)
 for child in source.rglob('*'):
  if child.is_file():
   dest=target/child.relative_to(source);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(child,dest)
   if child.suffix=='.mat':
    text=dest.read_text(encoding='utf-8-sig');dest.write_text('\n'.join(line.rstrip() for line in text.splitlines())+'\n',encoding='utf-8')
 guid=re.search(r'guid: ([a-f0-9]{32})',Path('Client/Assets/Prototype/RiverValleyTown/RiverValleyCityArtCatalog.cs.meta').read_text()).group(1)
 catalog=target/'Resources/Prototype/RiverValleyCityArt.asset'
 text=catalog.read_text(encoding='utf-8-sig');text=re.sub(r'(m_Script: \{fileID: 11500000, guid: )[a-f0-9]+',lambda match:match[1]+guid,text)
 catalog.write_text(text,encoding='utf-8')
 print('已同步 City 派生资源并关联主项目目录脚本')
