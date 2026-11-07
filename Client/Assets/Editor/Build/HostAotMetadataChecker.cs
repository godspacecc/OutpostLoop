#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>只从指定基线目录解析 AOT 和已验证的 facade，检查类型及成员；不查询 GAC、编辑器已加载程序集或其它搜索路径。</summary>
    public sealed class HostAotMetadataChecker : IAssemblyResolver, IDisposable
    {
        private readonly string directory;
        private readonly ModuleContext context;
        private readonly Dictionary<string, ModuleDefMD> modules = new Dictionary<string, ModuleDefMD>(StringComparer.Ordinal);

        public HostAotMetadataChecker(string directory)
        {
            this.directory = directory;
            context = new ModuleContext(this);
        }

        public AssemblyDef Resolve(IAssembly assembly, ModuleDef sourceModule)
        {
            string name = assembly.Name;
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.Contains(":") || name.Contains("\\") || name.Contains("/"))
                return null;
            if (!modules.TryGetValue(name, out var module))
            {
                string path = Path.Combine(directory, name + ".dll");
                if (!File.Exists(path)) return null;
                module = ModuleDefMD.Load(File.ReadAllBytes(path), context);
                modules.Add(name, module);
            }
            return module.Assembly != null && AssemblyNameComparer.CompareAll.Equals(module.Assembly, assembly)
                ? module.Assembly : null;
        }

        public bool Check(string hotDll)
        {
            using var hot = ModuleDefMD.Load(File.ReadAllBytes(hotDll), context);
            bool valid = true;
            foreach (var reference in hot.GetAssemblyRefs())
            {
                if (reference.Name == "Game") continue;
                if (Resolve(reference, hot) != null) continue;
                Debug.LogError("[HostAotMetadataChecker] 缺少 AOT 程序集或身份不符：" + reference.FullName);
                valid = false;
            }
            foreach (var type in hot.GetTypeRefs())
            {
                if (type.DefinitionAssembly.Name == "Game") continue;
                if (type.ResolveTypeDef() != null) continue;
                Debug.LogError("[HostAotMetadataChecker] 缺少 AOT 类型：" + type.FullName);
                valid = false;
            }
            foreach (var member in hot.GetMemberRefs())
            {
                if (member.DeclaringType.DefinitionAssembly == null || member.DeclaringType.DefinitionAssembly.Name == "Game") continue;
                if (member.IsFieldRef ? member.ResolveField() != null : member.ResolveMethod() != null) continue;
                // 多维数组构造/访问器由运行时提供，遵循 HybridCLR 原检查器的规则。
                var type = member.DeclaringType.ToTypeSig();
                if (!member.IsFieldRef && (type.ElementType == ElementType.Array || type.ElementType == ElementType.SZArray)) continue;
                Debug.LogError("[HostAotMetadataChecker] 缺少 AOT 成员：" + member.FullName);
                valid = false;
            }
            return valid;
        }

        public void Dispose()
        {
            foreach (var module in modules.Values) module.Dispose();
            modules.Clear();
        }
    }
}
#endif
