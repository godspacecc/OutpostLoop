from pathlib import Path
import tarfile,re,json,hashlib
out=Path('Backup/Artifacts/CityStreetPreview-20261118'); project=Path('Backup/Artifacts/PlayerMotion-20261118/PreviewProject')
pkg=next(Path('C:/Users/Administrator/AppData/Roaming/Unity/Asset Store-5.x/SyntyStudios').rglob('assetstore_package_20000945.unitypackage'))
with tarfile.open(pkg,'r:gz') as t:
 by={m.name:m for m in t}; paths={}
 for m in by.values():
  if m.name.endswith('/pathname'):paths[m.name.split('/')[0]]=t.extractfile(m).read().decode('utf-8-sig').splitlines()[0].split('\x00')[0]
 names=['SM_Bld_Shop_01','SM_Bld_Shop_03','SM_Bld_Apartment_01','SM_Env_Sidewalk_Straight_01','SM_Prop_Trashbin_01','SM_Prop_CardboardBox_03','SM_Veh_Car_Small_01','SM_Prop_ShopInterior_Shelf_01','SM_Prop_ShopInterior_Desk_01','SM_Bld_Shop_Cover_01','SM_Prop_LightPole_Base_01','SM_Prop_LightPole_Arm_01','SM_Prop_LightPole_Lights_01']
 seeds=[g for g,p in paths.items() if Path(p).stem in names and p.endswith('.prefab')];queue=list(seeds);done=set()
 while queue:
  g=queue.pop()
  if g in done or g not in paths:continue
  done.add(g)
  for part in ['asset','asset.meta']:
   m=by.get(g+'/'+part)
   if not m:continue
   data=t.extractfile(m).read()
   queue.extend(x.decode() for x in re.findall(rb'guid:\s*([a-f0-9]{32})',data))
   p=project/paths[g];p=p if part=='asset' else Path(str(p)+'.meta');p.parent.mkdir(parents=True,exist_ok=True)
   if not p.exists():p.write_bytes(data)
 report={'source':str(pkg),'sha256':hashlib.sha256(pkg.read_bytes()).hexdigest(),'selected':[paths[g] for g in seeds],'dependencies':[paths[g] for g in sorted(done)],'note':'只选取模型材质贴图与预制体依赖；不覆盖已有文件。缓存版本未由在线版本代认。'}
 (out/'selected-assets.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('selected',len(seeds),'dependencies',len(done))
