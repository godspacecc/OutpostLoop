using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        private MeshRenderer townRain;
        private Mesh townRainMesh;
        private Vector3[] rainPositions, rainVertices;
        private float rainTime;
        private AudioSource townAmbience, townGunAudio;
        private AudioClip rainClip, gunClip;
        private LineRenderer townGunTrace;
        private float townGunFlash;

        /// <summary>使用简单的屋体、道路和色块，让主街、住宅、仓储与镇郊容易分辨。</summary>
        private void CreateTownGeography(Material soil, Material road, Material concrete, Material steel, Material pad)
        {
            var asphalt = ScavengeMaterial(new Color(.24f, .27f, .29f));
            var yard = ScavengeMaterial(new Color(.38f, .40f, .38f));
            var brick = ScavengeMaterial(new Color(.58f, .36f, .27f));
            var plaster = ScavengeMaterial(new Color(.67f, .65f, .53f));
            var shop = ScavengeMaterial(new Color(.36f, .56f, .56f));
            var farm = ScavengeMaterial(new Color(.38f, .47f, .29f));
            var wood = ScavengeMaterial(new Color(.43f, .32f, .23f));
            ScavengeBox("镇郊地面", new Vector3(0, -.15f, 0), new Vector3(220, .29f, 194), soil);
            ScavengeBox("镇区地面", new Vector3(-9, -.045f, -19), new Vector3(132, .085f, 89), yard);
            Road("主街", -62, 0, 8, 146);
            Road("镇中公路", 0, -12, 169, 7);
            Road("北侧公路", 0, 22, 169, 7);
            Road("南北联络路", 2, 0, 7, 145);
            Road("仓区外环", 48, 0, 6, 145);
            for (int i = -13; i <= 13; i++)
            {
                ScavengeBox("主街标线", new Vector3(-62, .031f, 5 * i), new Vector3(.15f, .03f, 2.2f), plaster);
                ScavengeBox("公路标线", new Vector3(6 * i, .031f, -12), new Vector3(2.6f, .03f, .15f), plaster);
            }
            foreach (var ground in ScavengeTown.ShelterGrounds)
                ScavengeBox("可建用地", At(ground.center, .025f), new Vector3(ground.width, .03f, ground.height), pad);
            for (int i = 0; i < ScavengeTown.Buildings.Length; i++)
            {
                var footprint = ScavengeTown.Buildings[i];
                int room = survivalMode ? System.Array.IndexOf(SurvivalRooms.BuildingIds, i) : -1;
                if (room >= 0)
                {
                    CreateSurvivalRoom(room, plaster, concrete, wood, asphalt);
                    continue;
                }
                bool residential = i >= 3 && i < 7;
                bool industrial = i >= 10 && i < 14;
                float height = residential ? 3.9f : industrial ? 3.6f : 2.9f;
                var wall = ScavengeBox("建筑主体", At(footprint.center, height * .5f),
                    new Vector3(footprint.width, height, footprint.height), residential ? brick : industrial ? steel : plaster);
                var roof = ScavengeBox("建筑屋面", At(footprint.center, height + .08f),
                    new Vector3(footprint.width + .2f, .16f, footprint.height + .2f), industrial ? concrete : asphalt);
                var entrance = ScavengeBox("建筑入口", new Vector3(footprint.xMin - .04f, .92f, footprint.center.y),
                    new Vector3(.1f, 1.84f, 1.34f), steel);
                var canopy = ScavengeBox("门楣", new Vector3(footprint.xMin - .27f, 2.17f, footprint.center.y),
                    new Vector3(.66f, .2f, 2.48f), residential ? wood : shop);
                ScavengeBox("建筑轮廓", At(footprint.center, .04f), new Vector3(footprint.width, .08f, footprint.height), concrete);
                RegisterScavengeOccluder(wall.GetComponent<Renderer>(), roof.GetComponent<Renderer>(),
                    entrance.GetComponent<Renderer>(), canopy.GetComponent<Renderer>());
            }
            ScavengeBox("仓库地坪", new Vector3(32.5f, .01f, 8f), new Vector3(27f, .035f, 24f), concrete);
            foreach (var section in ScavengeTown.WarehouseWalls)
            {
                var wall = ScavengeBox("仓库墙", At(section.center, 1.15f), new Vector3(section.width, 2.3f, section.height), steel);
                ScavengeBox("仓库墙脚", At(section.center, .045f), new Vector3(section.width, .09f, section.height), concrete);
                RegisterScavengeOccluder(wall.GetComponent<Renderer>());
            }
            for (int row = 0; row < 9; row++)
                ScavengeBox("田垄", new Vector3(68 + row * 1.25f, .045f, 47), new Vector3(.65f, .06f, 22), farm);
            for (int i = 0; i < 9; i++)
                ScavengeBox("林带灌木", new Vector3(-78 + i * 3.8f, .21f, 58 + i % 3 * 2),
                    new Vector3(1.5f, .42f, 1.7f), farm);

            void Road(string name, float x, float z, float width, float length) =>
                ScavengeBox(name, new Vector3(x, .015f, z), new Vector3(width, .035f, length), asphalt);
        }

        /// <summary>幸存者、固定白昼中的雨效和临时战斗声音均属于当前探索场景。</summary>
        private void CreateTownActorsAndWeather(Material canvas, Material teal)
        {
            if (!survivalMode)
            {
                var survivor = Shape("本地幸存者", scavengeRoot, PrimitiveType.Capsule, At(ScavengeTown.Survivor, .76f),
                    new Vector3(.65f, .78f, .65f), teal);
                Shape("幸存者头部", survivor, PrimitiveType.Sphere, new Vector3(0, .68f, 0),
                    new Vector3(.51f, .45f, .51f), canvas);
            }
            var effects = new GameObject("镇区天气与声音");
            effects.transform.SetParent(scavengeRoot, false);
            townAmbience = effects.AddComponent<AudioSource>();
            townAmbience.loop = true;
            townAmbience.playOnAwake = false;
            townAmbience.volume = .22f;
            townGunAudio = effects.AddComponent<AudioSource>();
            townGunAudio.playOnAwake = false;
            rainClip = TownNoise("小雨", 3.5f, false);
            gunClip = TownNoise("手枪", .24f, true);
            townAmbience.clip = rainClip;
            var effectMaterial = new Material(Shader.Find("Sprites/Default"));
            scavengeMaterials.Add(effectMaterial);
            townGunTrace = CreateDefenseLine("手枪曳光", effectMaterial, 2, false, new Color(1f, .83f, .48f));
            townGunTrace.enabled = false;

            var weather = new GameObject("小雨");
            weather.transform.SetParent(scavengeRoot, false);
            townRain = weather.AddComponent<MeshRenderer>();
            var rainMaterial = new Material(effectMaterial) { color = new Color(.64f, .76f, .84f, .52f) };
            scavengeMaterials.Add(rainMaterial);
            townRain.sharedMaterial = rainMaterial;
            townRain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            townRain.enabled = false;
            townRainMesh = new Mesh { name = "临时雨线" };
            townRainMesh.MarkDynamic();
            weather.AddComponent<MeshFilter>().sharedMesh = townRainMesh;
            rainPositions = new Vector3[210];
            rainVertices = new Vector3[rainPositions.Length * 2];
            var indices = new int[rainVertices.Length];
            var random = new System.Random(21);
            for (int i = 0; i < rainPositions.Length; i++)
                rainPositions[i] = new Vector3((float)random.NextDouble() * 25 - 12.5f,
                    (float)random.NextDouble() * 8.5f, (float)random.NextDouble() * 25 - 12.5f);
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            townRainMesh.vertices = rainVertices;
            townRainMesh.SetIndices(indices, MeshTopology.Lines, 0);
            townRainMesh.bounds = new Bounds(new Vector3(0, 4.25f, 0), new Vector3(27, 10, 27));
        }

        /// <summary>生成可重复的短试听音，随探索退出释放。</summary>
        private static AudioClip TownNoise(string name, float seconds, bool impulse)
        {
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(rate * seconds)];
            var random = new System.Random(511);
            float low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float raw = (float)(random.NextDouble() * 2 - 1);
                low = Mathf.Lerp(low, raw, .13f);
                float t = (float)i / rate;
                samples[i] = impulse ? Mathf.Exp(-26 * t) * (.46f * raw + .53f * low) :
                    (.09f * raw + .29f * low) * Mathf.Clamp01(Mathf.Min(t, seconds - t) * 10f);
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void ResetTownWeather()
        {
            if (!scavengeTown) return;
            townGunFlash = 0;
            townGunTrace.enabled = false;
            rainTime = 0;
            townRain.enabled = scavengeRun.IsRain;
            townAmbience.Stop();
            UpdateTownWeather();
            if (!scavengeRun.IsRain) return;
            townAmbience.Play();
        }

        private void UpdateTownWeather()
        {
            if (!scavengeTown) return;
            townRain.transform.position = At(scavengeRun.Player, 0);
            if (townRain.enabled)
            {
                rainTime += Time.unscaledDeltaTime * 11f;
                for (int i = 0; i < rainPositions.Length; i++)
                {
                    var drop = rainPositions[i];
                    drop.y = Mathf.Repeat(drop.y - rainTime, 8.5f);
                    rainVertices[i * 2] = drop;
                    rainVertices[i * 2 + 1] = drop + new Vector3(-.045f, .42f, .04f);
                }
                townRainMesh.vertices = rainVertices;
            }
            townGunFlash = Mathf.Max(0, townGunFlash - Time.unscaledDeltaTime);
            townGunTrace.enabled = townGunFlash > 0;
            if (scavengeRun.Outcome != ScavengeOutcome.Exploring) townAmbience.Stop();
        }

        private void PlayTownShot(Vector2 aim)
        {
            if (!scavengeTown) return;
            townGunAudio.PlayOneShot(gunClip, .55f);
            townGunFlash = .12f;
            var origin = scavengeRun.Player;
            townGunTrace.SetPosition(0, At(origin + aim.normalized * .5f, .82f));
            townGunTrace.SetPosition(1, At(scavengeRun.GunshotEnd, .82f));
        }

        private void ReleaseTownAudio()
        {
            Release(townRainMesh);
            townRainMesh = null;
            Release(rainClip);
            Release(gunClip);
            rainClip = gunClip = null;
        }

        /// <summary>显示所在片区、天气、持枪操作与本趟线索。</summary>
        private void DrawTownStatus(float width, float height)
        {
            var run = scavengeRun;
            var point = run.Player;
            string district = point.y > 25 ? point.x > 12 ? "镇郊农场" : "林带与公路" :
                point.x > 17 ? "仓储区" : point.x < -41 ? "主街" : point.y < -26 ? "住宅后巷" : "镇中心";
            GUI.Box(new Rect(20, height - 105, 285, 35), district + (run.IsRain ? " · 白昼小雨" : " · 白昼阴天"));
            if ((run.Inventory & (ScavengeItem.Crowbar | ScavengeItem.Pistol)) != 0)
                GUI.Label(new Rect(22, height - 59, 330, 32), run.UsingFirearm ? $"手枪 {run.Rounds}/6 · 左键射击 · R 装填 · 1 撬棍" : "撬棍 · 左键挥击 · 2 手枪");
            if (run.MedicineLeadKnown || run.MedicineFound)
            {
                string lead = run.MedicineFound ? "已找到备用药品" : run.SurvivorSpoken ? "幸存者说还有备用药品" : "转运单 · 查找备用药品";
                string detail = run.MedicineFound ? "可继续探索或撤离" : run.CacheNames[run.MedicineCache];
                GUI.Box(new Rect(width - 246, height - 115, 226, 76), "");
                GUI.Label(new Rect(width - 234, height - 109, 202, 28), lead);
                GUI.Label(new Rect(width - 234, height - 79, 202, 28), detail);
            }
        }
    }
}
