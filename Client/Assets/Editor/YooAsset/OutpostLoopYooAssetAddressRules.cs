#if UNITY_EDITOR
using System;
using System.IO;

namespace YooAsset.Editor
{
    [DisplayName("定位地址: 文件名含扩展名")]
    public sealed class AddressByFileNameWithExtension : IAddressRule
    {
        string IAddressRule.GetAssetAddress(AddressRuleData data)
        {
            return Path.GetFileName(data.AssetPath);
        }
    }

    [DisplayName("定位地址: Game相对路径无扩展名")]
    public sealed class AddressByGameRelativePathNoExtension : IAddressRule
    {
        private const string GameRoot = "Assets/Game/";

        string IAddressRule.GetAssetAddress(AddressRuleData data)
        {
            string assetPath = data.AssetPath.Replace('\\', '/');
            if (!assetPath.StartsWith(GameRoot, StringComparison.Ordinal))
            {
                return Path.GetFileNameWithoutExtension(assetPath);
            }

            string relativePath = assetPath.Substring(GameRoot.Length);
            string directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(relativePath);
            return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
        }
    }
}
#endif
