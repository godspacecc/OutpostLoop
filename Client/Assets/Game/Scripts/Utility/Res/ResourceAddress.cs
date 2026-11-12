using System;

namespace Game
{
    public enum ResourceAddressKind
    {
        CharacterPrefab,
        WeaponAppearancePrefab,
        SceneObjectPrefab,
        ProjectilePrefab,
        ItemIcon = 5,
        MapScene = 7,
        UiPrefab,
        Music,
        Sound
    }

    /// <summary>
    /// 仅负责把已知类别的最小资源标识解析为完整逻辑地址。
    /// </summary>
    public static class ResourceAddress
    {
        private static readonly string[] ResourceExtensions =
        {
            ".prefab", ".png", ".jpg", ".jpeg", ".tga", ".psd", ".wav", ".mp3", ".ogg", ".aiff", ".unity"
        };

        public static bool TryFromFileName(ResourceAddressKind kind, string value, bool optional, out string address, out string error)
        {
            address = string.Empty;
            error = null;
            if (!IsFileNameKind(kind))
            {
                error = $"资源类别 {kind} 不接受文件名标识";
                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                if (optional)
                {
                    return true;
                }

                error = "文件名不能为空";
                return false;
            }

            var name = value.Trim();
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
            {
                error = "文件名不能包含路径分隔符";
                return false;
            }

            if (HasResourceExtension(name))
            {
                error = "文件名不能包含资源扩展名";
                return false;
            }

            address = GetRoot(kind) + name;
            return true;
        }

        public static bool TryFromRelativePath(ResourceAddressKind kind, string value, out string address, out string error)
        {
            address = string.Empty;
            error = null;
            if (kind != ResourceAddressKind.MapScene && kind != ResourceAddressKind.UiPrefab)
            {
                error = $"资源类别 {kind} 不接受相对路径标识";
                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "相对路径不能为空";
                return false;
            }

            var path = value.Trim();
            if (path.IndexOf('\\') >= 0)
            {
                error = "相对路径只能使用 /";
                return false;
            }

            if (path.StartsWith("/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
            {
                error = "相对路径不能以 / 开头或结尾";
                return false;
            }

            var root = GetRoot(kind);
            if (path.StartsWith(root, StringComparison.Ordinal))
            {
                error = $"相对路径不能包含公共根目录 {root}";
                return false;
            }

            var segments = path.Split('/');
            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment))
                {
                    error = "相对路径不能包含空路径段";
                    return false;
                }

                if (segment == "." || segment == "..")
                {
                    error = "相对路径不能包含 . 或 ..";
                    return false;
                }
            }

            if (HasResourceExtension(path))
            {
                error = "相对路径不能包含资源扩展名";
                return false;
            }

            address = root + path;
            return true;
        }

        private static bool IsFileNameKind(ResourceAddressKind kind)
        {
            return kind != ResourceAddressKind.MapScene && kind != ResourceAddressKind.UiPrefab;
        }

        private static bool HasResourceExtension(string value)
        {
            foreach (var extension in ResourceExtensions)
            {
                if (value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetRoot(ResourceAddressKind kind)
        {
            switch (kind)
            {
                case ResourceAddressKind.CharacterPrefab: return "Prefabs/Characters/";
                case ResourceAddressKind.WeaponAppearancePrefab: return "Prefabs/Characters/Weapons/";
                case ResourceAddressKind.SceneObjectPrefab: return "Prefabs/SceneObjects/";
                case ResourceAddressKind.ProjectilePrefab: return "Prefabs/Combat/";
                case ResourceAddressKind.ItemIcon: return "Art/UI/Items/";
                case ResourceAddressKind.MapScene: return "Scenes/";
                case ResourceAddressKind.UiPrefab: return "Prefabs/UI/";
                case ResourceAddressKind.Music: return "Audio/Music/";
                case ResourceAddressKind.Sound: return "Audio/Sound/";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}
