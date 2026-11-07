#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
namespace Game.Editor
{
    /// <summary>冻结 Android 快速包底层证据并拒绝新增原生需求。</summary>
    public static class AndroidQuickAotUtility
    {
        /// <summary>保存 Game 参照和元数据名单，已有记录不得覆盖。</summary>
        public static void Preserve(string directory)
        {
            CopyOnce(Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), "Game.dll.bytes"), Path.Combine(directory, "baseline-game.bytes"));
            // 复用本项目配对宿主身份，不能从当前源码重新生成身份。
            CopyOnce(Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), Boot.HostCompatibilityDescriptor.AssetAddress), Path.Combine(directory, Boot.HostCompatibilityDescriptor.AssetAddress));
            CopyOnce(HybridCLRBuildUtility.GetRuntimeListPath(), Path.Combine(directory, "runtime-list.bytes"));
        }
        /// <summary>已有文件必须一致。</summary>
        private static void CopyOnce(string source, string target)
        {
            if (!File.Exists(target)) File.Copy(source, target);
            else if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target))) throw new InvalidOperationException("冻结 AOT 参照不允许覆盖。");
        }
        /// <summary>包含非 Game 代码、插件、编译布局和生成桥接。</summary>
        public static string HostFingerprint()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new InvalidOperationException("复用 AOT 必须在 Android 平台。");
            string root = Directory.GetParent(Application.dataPath).FullName;
            var sources = CompilationPipeline.GetAssemblies(AssembliesType.Player).Where(a => a.name != "Game")
                .SelectMany(a => a.sourceFiles.Concat(a.compiledAssemblyReferences.Where(p => !p.Replace('\\', '/').Contains("/Library/"))));
            var assets = Directory.GetFiles(Application.dataPath, "*", SearchOption.AllDirectories).Where(p =>
                p.Replace('\\', '/').Contains("/Plugins/") || p.Replace('\\', '/').Contains("/HybridCLRGenerate/") ||
                new[] { ".asmdef", ".asmref", ".rsp" }.Contains(Path.GetExtension(p)));
            assets = assets.Concat(Directory.GetFiles(Path.Combine(root, "ProjectSettings"), "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p) != "ProjectSettings.asset"));
            return AndroidBootBuildGuard.ComputeFingerprint(root) + "|" + AndroidQuickBuildUtility.FingerprintFiles(sources.Concat(assets)
                .Concat(CollectHostAssetInputs(root, AssetDatabase.GetDependencies("Assets/Game/Scenes/Boot.unity", true)))
                .SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p })
                .Concat(Directory.GetFiles(Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir), "*", SearchOption.AllDirectories))
                .Concat(new[] { Path.Combine(root,"Packages","manifest.json"), Path.Combine(root,"Packages","packages-lock.json") }).ToArray());
        }
        /// <summary>只收集直接进入宿主的资源，不把普通远程美术冻结为 AOT 输入。</summary>
        public static string[] CollectHostAssetInputs(string root, string[] bootDependencies)
        {
            string assets = Path.Combine(root, "Assets");
            return Directory.GetFiles(assets, "*", SearchOption.AllDirectories).Where(path =>
            {
                string relative = Path.GetRelativePath(assets, path).Replace('\\', '/');
                bool resources = relative.StartsWith("Resources/", StringComparison.Ordinal) || relative.Contains("/Resources/");
                bool streaming = relative.StartsWith("StreamingAssets/", StringComparison.Ordinal) &&
                    !relative.StartsWith("StreamingAssets/yoo/", StringComparison.Ordinal) && relative != "StreamingAssets/yoo.meta" &&
                    relative != "StreamingAssets/windows-release.json" && relative != "StreamingAssets/windows-release.json.meta";
                return resources || streaming;
            }).Concat(bootDependencies.Where(File.Exists)).SelectMany(path => File.Exists(path + ".meta") ? new[] { path, path + ".meta" } : new[] { path })
                .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>新增外部泛型或原生入口无法证明桥接兼容时要求完整准备。</summary>
        public static void ValidateGame(string baseline, string current)
        {
            if (File.ReadAllBytes(baseline).SequenceEqual(File.ReadAllBytes(current))) return;
            ValidateManagedReferences(baseline, current);
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(current)))
                foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
                    if (method.IsPinvokeImpl || method.CustomAttributes.Any(a => a.TypeFullName.Contains("MonoPInvokeCallback") || a.TypeFullName.Contains("UnmanagedCallersOnly")) ||
                        (method.HasBody && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Calli)))
                        throw new InvalidOperationException("Game 存在原生调用或回调需求，必须重新完整准备 AOT。");
        }
        /// <summary>新外部类型/成员可能已被旧宿主裁掉；只允许初始Game已使用的需求集合。</summary>
        internal static void ValidateManagedReferences(string baselineGame, string currentGame)
        {
            var supported = ExternalRequirements(baselineGame);
            string added = ExternalRequirements(currentGame).FirstOrDefault(requirement => !supported.Contains(requirement));
            if (added != null)
                throw new InvalidOperationException("热更Game新增外部类型或成员需求，无法确认旧宿主已保留：" + added + "\n" + "请重新完整准备 Android AOT。");
        }

        /// <summary>从真实DLL读取外部引用与具体泛型调用，不执行Game或生成新的宿主裁剪文件。</summary>
        private static HashSet<string> ExternalRequirements(string dll)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(dll)))
            {
                string ownAssembly = module.Assembly?.FullName;
                Action<ITypeDefOrRef, string> add = (type, requirement) =>
                {
                    string assembly = type?.DefinitionAssembly?.FullName;
                    if (assembly != null && assembly != ownAssembly) result.Add(assembly + "|" + requirement);
                };
                foreach (var type in module.GetTypeRefs()) add(type, type.FullName);
                foreach (var member in module.GetMemberRefs()) add(member.DeclaringType, member.FullName);
                foreach (var method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is IMethod called) add(called.DeclaringType, called.FullName);
                        else if (instruction.Operand is IField field) add(field.DeclaringType, field.FullName);
                        else if (instruction.Operand is ITypeDefOrRef type) add(type, type.FullName);
                    }
            }
            return result;
        }

    }
}
#endif
