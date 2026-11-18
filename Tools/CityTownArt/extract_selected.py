from pathlib import Path
import tarfile,re,json,hashlib
root=Path('Backup/Artifacts/PlayerMotion-20261118/PreviewProject');out=Path('Backup/Artifacts/CityTownReplacement-20261118')
# 只选择已授权城市/通用环境原件，依赖闭包只读来自用户自有缓存。
select={
'roof':'SM_Bld_Apartment_Roof_01','shop':'SM_Bld_Shop_01','shopAlt':'SM_Bld_Shop_03','residential':'SM_Bld_Apartment_01',
'wall':'SM_Bld_Base_Wall_01','window':'SM_Bld_Base_Wall_Window_01','floor':'SM_Bld_Base_Floor_01','ceiling':'SM_Bld_Base_Ceiling_01','door':'SM_Bld_Base_Door_01',
'crate':'SM_Gen_Prop_Crate_01','shelf':'SM_Prop_ShopInterior_Shelf_01','desk':'SM_Prop_ShopInterior_Desk_01',
'road':'SM_Env_Road_01','sidewalk':'SM_Env_Sidewalk_Straight_01','bridgeRail':'SM_Env_Bridge_Wall_01','bridgePillar':'SM_Env_Bridge_Pillar_01',
'car':'SM_Veh_Car_Sedan_01','van':'SM_Veh_Car_Van_01','ambulance':'SM_Veh_Car_Ambo_01',
'pine':'SM_Gen_Env_Tree_Pine_01','tree':'SM_Gen_Env_Tree_01','treeAlt':'SM_Gen_Env_Tree_03','bush':'SM_Gen_Env_Bush_04',
'bin':'SM_Prop_Trashbin_01','lampBase':'SM_Prop_LightPole_Base_01','lampArm':'SM_Prop_LightPole_Arm_01','lampHead':'SM_Prop_LightPole_Lights_01',
'hospitalSign':'SM_Prop_Sign_Hospital_01','fence':'SM_Env_Fence_01'}
pkg=next(Path('C:/Users/Administrator/AppData/Roaming/Unity/Asset Store-5.x/SyntyStudios').rglob('assetstore_package_20000945.unitypackage'))
with tarfile.open(pkg,'r:gz') as t:
 by={m.name:m for m in t};paths={}
 for m in by.values():
  if m.name.endswith('/pathname'):paths[m.name.split('/')[0]]=t.extractfile(m).read().decode('utf-8-sig').splitlines()[0].split('\x00')[0]
 chosen={k:next(p for p in paths.values() if p.endswith('/'+n+'.prefab')) for k,n in select.items()};pending=[g for g,p in paths.items() if p in chosen.values()];done=set()
 while pending:
  g=pending.pop()
  if g in done or g not in paths:continue
  done.add(g)
  for part in ['asset','asset.meta']:
   m=by.get(g+'/'+part)
   if not m:continue
   data=t.extractfile(m).read();pending += [v.decode() for v in re.findall(rb'guid:\s*([a-f0-9]{32})',data)]
   dest=root/paths[g];dest=dest if part=='asset' else Path(str(dest)+'.meta');dest.parent.mkdir(parents=True,exist_ok=True)
   if not dest.exists():dest.write_bytes(data)
 (out/'selection.json').write_text(json.dumps({'package':str(pkg),'sha256':hashlib.sha256(pkg.read_bytes()).hexdigest(),'selected':chosen,'dependencies':[paths[g] for g in done]},ensure_ascii=False,indent=2),encoding='utf-8')
 print(len(chosen),'sources',len(done),'dependencies')
