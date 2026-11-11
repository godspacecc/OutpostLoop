using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 每次重开随临时材质一起释放港区纹理。
        private readonly System.Collections.Generic.List<Texture2D> harborTextures = new System.Collections.Generic.List<Texture2D>();

        /// <summary>从开始页直接进入旧港河堤的六波守城局。</summary>
        internal void StartOldHarbor()
        {
            if (!initialized || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) return;
            ShowWorld(session.BeginOldHarbor(++seed), true);
            RefreshOverlay();
        }

        /// <summary>河堤样片使用临时几何体。墙脚和水面边缘严格对齐规则占地。</summary>
        private void CreateOldHarborTerrain(Material concrete, Material steel, Material safety)
        {
            var water = Mat(new Color(.15f, .29f, .32f));
            var shallow = Mat(new Color(.34f, .42f, .30f));
            var seam = Mat(new Color(.29f, .32f, .30f));
            var cargo = Mat(new Color(.28f, .40f, .42f));
            var rust = Mat(new Color(.58f, .32f, .23f));
            water.SetFloat("_Glossiness", .48f);
            shallow.SetFloat("_Glossiness", .26f);
            var basin = OldHarborLayout.Basin;
            var shore = BattleGrid.Center(basin);
            float width = basin.width * BattleGrid.CellSize;
            float length = basin.height * BattleGrid.CellSize;
            HarborBox("HarborBasin", worldRoot, At(shore, -.093f), new Vector3(width, .12f, length), water);
            float bank = shore.x - width * .5f;
            HarborBox("HarborShallows", worldRoot, new Vector3(bank + 2f, -.076f, shore.y),
                new Vector3(4f, .12f, length), shallow);
            for (int i = 0; i < basin.height; i++)
            {
                float z = (basin.yMin + i) * BattleGrid.CellSize;
                HarborBox("ShoreMark", worldRoot, new Vector3(bank - .12f, -.041f, z),
                    new Vector3(.22f, .025f, 1.18f), safety);
                HarborBox("WaterRipple", worldRoot, new Vector3(bank + 1.2f + (i % 3) * .8f, -.007f, z),
                    new Vector3(1.3f + (i % 2) * .55f, .015f, .045f), water);
            }

            var levee = OldHarborLayout.Levee;
            var wall = BattleGrid.Center(levee);
            float wallWidth = levee.width * BattleGrid.CellSize;
            float wallDepth = levee.height * BattleGrid.CellSize;
            HarborBox("HarborLevee", worldRoot, At(wall, 1.42f), new Vector3(wallWidth, 2.94f, wallDepth), concrete);
            HarborBox("LeveeCoping", worldRoot, At(wall, 2.96f), new Vector3(wallWidth, .16f, wallDepth), steel);
            for (int i = 0; i < levee.width; i++)
            {
                float x = (levee.xMin + i) * BattleGrid.CellSize;
                float front = wall.y - wallDepth * .5f;
                HarborBox("LeveeJoint", worldRoot, new Vector3(x, 1.38f, front + .04f),
                    new Vector3(.055f, 2.76f, .08f), seam);
                HarborBox("LeveeWarning", worldRoot, new Vector3(x, 2.39f, front + .035f),
                    new Vector3(1.45f, .16f, .08f), safety);
                HarborBox("LeveeRailPost", worldRoot, new Vector3(x, 3.42f, front + .16f),
                    new Vector3(.09f, .85f, .09f), safety);
            }
            HarborBox("LeveeRail", worldRoot, new Vector3(wall.x, 3.83f, wall.y - wallDepth * .5f + .16f),
                new Vector3(wallWidth - .24f, .08f, .08f), safety);
            for (int i = 0; i < 3; i++)
            {
                float x = wall.x + (i - 1) * 7.5f;
                var paint = i == 1 ? rust : cargo;
                HarborBox("HarborContainer", worldRoot, new Vector3(x, 3.99f, wall.y + .45f),
                    new Vector3(5.35f, 1.9f, 2.15f), paint);
                for (int rib = 0; rib < 11; rib++)
                    HarborBox("ContainerRib", worldRoot, new Vector3(x - 2.5f + rib * .5f, 3.99f, wall.y - .64f),
                        new Vector3(.09f, 1.77f, .10f), paint);
            }

            // 控制台固定在水区的禁建格内，供玩家辨认河堤地标。
            float gateX = bank + 2.6f;
            HarborBox("HarborSluiceFooting", worldRoot, new Vector3(gateX, .15f, -1), new Vector3(4.7f, .35f, 4.5f), concrete);
            HarborBox("HarborSluice", worldRoot, new Vector3(gateX - .5f, 1.18f, -1), new Vector3(2.65f, 1.82f, 2.9f), steel);
            HarborBox("SluiceRoof", worldRoot, new Vector3(gateX - .5f, 2.16f, -1), new Vector3(2.93f, .15f, 3.25f), seam);
            for (int i = 0; i < 8; i++)
                HarborBox("RoofRib", worldRoot, new Vector3(gateX - 1.76f + i * .36f, 2.26f, -1),
                    new Vector3(.07f, .08f, 3.2f), steel);
            HarborBox("SluicePlatform", worldRoot, new Vector3(gateX + 1.5f, .67f, -1), new Vector3(2.8f, .18f, 1.3f), steel);
            var hub = new Vector3(gateX + 1.15f, 1.35f, -1.65f);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                var radius = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                var rim = HarborBox("SluiceHandwheel", worldRoot, hub + radius * .61f,
                    new Vector3(.11f, .35f, .12f), rust);
                rim.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                if (i % 3 == 0)
                {
                    var spoke = HarborBox("HandwheelSpoke", worldRoot, hub + radius * .3f,
                        new Vector3(.60f, .075f, .075f), rust);
                    spoke.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                }
            }

            // 地面标线保留干地的可选站址，也引导玩家寻找堤身两端。
            for (int i = 0; i < 9; i++)
            {
                float z = -17 + i * 3;
                HarborBox("ServiceRoadDash", worldRoot, new Vector3(-10, -.038f, z), new Vector3(.14f, .025f, 1.5f), safety);
                HarborBox("ConcreteJoint", worldRoot, new Vector3(-2f, -.039f, z - 1.2f), new Vector3(29f, .024f, .032f), seam);
            }
        }

        /// <summary>主站落位后随现有基地坐标显示港口主站外观。</summary>
        private void CreateOldHarborHeadquarters(Material concrete, Material steel)
        {
            var sand = Mat(new Color(.73f, .58f, .28f));
            HarborBox("HarborBasePad", headquarters, new Vector3(0, -.015f, 0), new Vector3(5.9f, .13f, 5.9f), concrete);
            HarborBox("HarborBaseWalls", headquarters, new Vector3(0, 1.18f, 0), new Vector3(4.78f, 2.35f, 3.8f), sand);
            HarborBox("HarborBaseRoof", headquarters, new Vector3(0, 2.43f, 0), new Vector3(5.1f, .26f, 4.1f), concrete);
            HarborBox("HarborBaseDoor", headquarters, new Vector3(.72f, .91f, -1.93f), new Vector3(1.18f, 1.82f, .11f), steel);
            for (int i = 0; i < 3; i++)
                HarborBox("HarborBaseWindow", headquarters, new Vector3(-1.98f + i * .61f, 1.63f, -1.93f),
                    new Vector3(.47f, .47f, .09f), steel);
            HarborBox("RoofGenerator", headquarters, new Vector3(-.93f, 2.88f, .52f), new Vector3(1.86f, .64f, .95f), sand);
            HarborBox("GeneratorVent", headquarters, new Vector3(-.93f, 2.88f, .02f), new Vector3(1.26f, .31f, .045f), steel);
            HarborBox("GeneratorExhaust", headquarters, new Vector3(-.24f, 3.23f, .64f), new Vector3(.16f, .47f, .16f), steel);
        }

        private Transform HarborBox(string label, Transform parent, Vector3 position, Vector3 scale, Material material) =>
            Shape(label, parent, PrimitiveType.Cube, position, scale, material);

        /// <summary>用低对比磨损给临时混凝土和港区路面增加远近两层纹理。</summary>
        private void AddHarborSurface(Material material, float repeat, float wear)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float broad = Mathf.PerlinNoise(8 + x * .1f, 4 + y * .11f);
                float grain = Mathf.PerlinNoise(24 + x * .47f, 28 + y * .41f);
                float shade = 1 - (broad * .65f + grain * .35f) * wear;
                pixels[y * size + x] = new Color(shade, shade, shade);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one * repeat;
            harborTextures.Add(texture);
        }

        /// <summary>感染体复用原敌人的对象池，增加在俯视下可辨的头部和四肢。</summary>
        private void DressHarborEnemy(Transform body, Material skin, Material trousers)
        {
            Shape("Head", body, PrimitiveType.Sphere, new Vector3(0, .86f, .13f), new Vector3(.48f, .38f, .44f), skin);
            for (int side = -1; side <= 1; side += 2)
            {
                var arm = Shape("Arm", body, PrimitiveType.Capsule, new Vector3(side * .53f, .13f, .22f),
                    new Vector3(.21f, .39f, .22f), skin);
                arm.localRotation = Quaternion.Euler(35, 0, side * 18);
                Shape("Leg", body, PrimitiveType.Capsule, new Vector3(side * .25f, -.59f, 0),
                    new Vector3(.35f, .4f, .38f), trousers);
            }
        }
    }
}
