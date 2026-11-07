using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class ApplyDisplaySettingsCommand : AbstractCommand
    {
        public string ResolutionCode;
        public FullScreenMode WindowMode = FullScreenMode.Windowed;
        public bool Commit;

        protected override void OnExecute()
        {
            var preset = DisplayResolutionPresets.FindOrDefault(ResolutionCode);
            var windowMode = DisplaySettingsModel.SanitizeWindowMode(WindowMode);
            var model = this.GetModel<DisplaySettingsModel>();

            model.SetResolutionCode(preset.Code);
            model.SetWindowMode(windowMode);
            this.GetUtility<IDisplayResolutionApplier>().Apply(preset, windowMode);

            if (!Commit)
            {
                return;
            }

            this.GetSystem<SettingsSystem>().MarkDirty();
        }
    }
}
