using System;
using System.Collections.Generic;
using Boot;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static OutpostLoop.PrototypeMenuWidgets;

namespace OutpostLoop
{
    internal sealed partial class PrototypeSettingsView
    {
        private enum Section { General, Audio, Controls, Updates }
        private static readonly string[] SectionKeys = {"SettingsGeneral", "SettingsAudio", "SettingsKeys", "SettingsUpdates"};
        private const float SectionWidth = 734;
        // 设置分组只决定当前显示；候选与已提交设置仍由现有事务管理。
        private readonly List<Button> navigation = new();
        private PrototypeMenuWidgets widgets;
        private RectTransform content, settingsPanel;
        private PerceptionLayoutView perceptionEditor;
        private ScrollRect scrollView;
        private TMP_Text title, displayCountdown;
        private Button back;
        private IUnRegister captured;
        private Section section;
        private string languageCode, conflictAction;
        private bool pageDirty, displayWasActive, wasCapturing;
        private int escapeCaptureFrame = -1;
        private ResourceUpdateStatus updateStatus;

        private void OpenMenu()
        {
            if (!content) CreateMenu();
            conflictAction = null;
            captured?.UnRegister();
            captured = Architecture.RegisterEvent<HotKeyRebindCapturedEvent>(OnRebindCaptured);
            wasCapturing = false;
            displayWasActive = false;
            SelectSection(Section.General);
        }

        private void CloseMenu()
        {
            ClosePerception();
            captured?.UnRegister();
            captured = null;
        }

        private void CreateMenu()
        {
            widgets = new PrototypeMenuWidgets();
            var panel = settingsPanel = Panel(transform, "SettingsPanel", 1100, 770);
            title = widgets.Label(panel, "Title", 34, 20, 940, 68, "", 39, TextColor);
            Graphic(panel, "TitleRule", 34, 99, 1032, 2, Line);
            for (int i = 0; i < SectionKeys.Length; i++)
            {
                var target = (Section)i;
                navigation.Add(widgets.Button(panel, "SettingsNav" + target, 34, 130 + i * 78, 235, 62, "",
                    () => SelectSection(target), 25));
            }

            var viewport = Graphic(panel, "SettingsViewport", 302, 128, 764, 541, Well, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect(viewport.transform, "SettingsRows", 0, 0, 764, 541);
            scrollView = viewport.gameObject.AddComponent<ScrollRect>();
            scrollView.viewport = viewport.rectTransform;
            scrollView.content = content;
            scrollView.horizontal = false;
            scrollView.movementType = ScrollRect.MovementType.Clamped;
            scrollView.scrollSensitivity = 60;
            Graphic(panel, "FooterRule", 34, 686, 1032, 2, Line);
            back = widgets.Button(panel, "SettingsBack", 825, 702, 241, 48, "", HandleBack, 24, true);
        }

        private void SelectSection(Section next)
        {
            section = next;
            pageDirty = true;
            scrollView.StopMovement();
            content.anchoredPosition = Vector2.zero;
            RenderMenu();
        }

        private void LateUpdate()
        {
            if (!content || !FoundationRuntime.IsInitialized) return;
            bool esc = Keyboard.current?.escapeKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Escape);
            if (perceptionEditor)
            {
                if (esc) perceptionEditor.Escape();
                return;
            }
            if (esc && escapeCaptureFrame != Time.frameCount) HandleBack();
            if (!isActiveAndEnabled) return;

            var preview = Architecture.GetSystem<DisplayPreviewTransaction>();
            if (displayWasActive != preview.IsActive)
            {
                if (!preview.IsActive) LoadDisplay();
                pageDirty = true;
            }
            displayWasActive = preview.IsActive;
            bool capturingKey = Architecture.GetSystem<HotKeySystem>().IsCapturingRebind;
            if (capturingKey != wasCapturing) pageDirty = true;
            wasCapturing = capturingKey;
            var status = Architecture.GetSystem<ResourceUpdateSystem>().Snapshot.Status;
            if (status != updateStatus && section == Section.Updates) pageDirty = true;
            updateStatus = status;

            RenderMenu();
            if (preview.IsActive && displayCountdown)
                displayCountdown.text = Loc.T("SettingsCountdown", Mathf.CeilToInt(preview.RemainingSeconds));
        }

        /// <summary>Esc 先取消重绑、试用或更新确认，常态下返回打开设置的页面。</summary>
        private void HandleBack()
        {
            var keys = Architecture.GetSystem<HotKeySystem>();
            if (keys.IsCapturingRebind)
            {
                keys.CancelRebind();
                conflictAction = null;
            }
            else if (Architecture.GetSystem<DisplayPreviewTransaction>().IsActive) CancelDisplayPreview();
            else if (restartPending) restartPending = false;
            else
            {
                Close?.Invoke();
                return;
            }
            pageDirty = true;
        }

        private void OnRebindCaptured(HotKeyRebindCapturedEvent evt)
        {
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                escapeCaptureFrame = Time.frameCount;
            conflictAction = evt.Result.ConflictingActionId;
            if (!evt.Result.NeedsConfirmation && !evt.Result.Success && escapeCaptureFrame != Time.frameCount)
                message = evt.Result.Error;
            pageDirty = true;
        }

        private void RenderMenu()
        {
            string current = Architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            if (!pageDirty && current == languageCode) return;
            pageDirty = false;
            languageCode = current;
            title.text = Loc.T("SettingsTitle");
            bool confirming = Architecture.GetSystem<DisplayPreviewTransaction>().IsActive ||
                Architecture.GetSystem<HotKeySystem>().IsCapturingRebind || restartPending;
            Caption(back, Loc.T(confirming ? "CommonCancel" : "CommonBack") + "  Esc");
            for (int i = 0; i < navigation.Count; i++)
            {
                Caption(navigation[i], Loc.T(SectionKeys[i]));
                navigation[i].interactable = !confirming;
                Style(navigation[i], (int)section == i);
            }
            displayCountdown = null;
            foreach (Transform row in content)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            float height = section switch
            {
                Section.General => RenderGeneral(),
                Section.Audio => RenderAudio(),
                Section.Controls => RenderControls(),
                _ => RenderUpdates()
            };
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(541, height + 12));
        }

        private TMP_Text LineText(string key, float y, string value, bool wrap = false, float height = 46, bool alert = false) =>
            widgets.Label(content, key, 16, y, SectionWidth - 16, height, value, 24, alert ? Warning : TextColor, wrap);

        private Button RowButton(string name, float x, float y, float width, string value, Action action, bool selected = false) =>
            widgets.Button(content, name, x, y, width, 54, value, action, 23, selected);

        private float RenderGeneral()
        {
            LineText("LanguageHeading", 12, Loc.T("SettingsLanguage"));
            var language = Architecture.GetModel<LocalizationModel>();
            int index = 0;
            foreach (var item in LocalizationLanguages.All)
            {
                RowButton("Language-" + item.Code, 16 + 220 * index++, 65, 204, item.Label, () =>
                {
                    language.SetLanguageCode(item.Code);
                    Architecture.GetSystem<SettingsSystem>().MarkDirty();
                }, item.Code == languageCode);
            }

            LineText("DisplayHeading", 142, Loc.T("SettingsDisplay"));
            var preview = Architecture.GetSystem<DisplayPreviewTransaction>();
            index = 0;
            foreach (var preset in DisplayResolutionPresets.All)
            {
                RowButton("Resolution-" + preset.Code, 16 + index++ * 201, 193, 186, preset.Label, () =>
                {
                    resolution = preset.Code;
                    pageDirty = true;
                }, resolution == preset.Code).interactable = !preview.IsActive;
            }
            bool full = mode == FullScreenMode.FullScreenWindow;
            RowButton("WindowMode", 16, 260, 278, Loc.T(full ? "SettingsFullscreen" : "SettingsWindowed"), () =>
            {
                mode = full ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                pageDirty = true;
            }).interactable = !preview.IsActive;
            if (preview.IsActive)
            {
                displayCountdown = LineText("DisplayCountdown", 329,
                    Loc.T("SettingsCountdown", Mathf.CeilToInt(preview.RemainingSeconds)), alert: true);
                RowButton("ConfirmDisplay", 16, 377, 205, Loc.T("CommonConfirm"), () =>
                {
                    preview.Confirm();
                    LoadDisplay();
                    pageDirty = true;
                }, true);
                RowButton("CancelDisplay", 233, 377, 205, Loc.T("CommonCancel"), CancelDisplayPreview);
            }
            else RowButton("PreviewDisplay", 16, 329, 262, Loc.T("SettingsPreview"), () =>
            {
                preview.BeginPreview(resolution, mode);
                pageDirty = true;
            });
            RowButton("ResetSettings", 16, 454, 262, Loc.T("SettingsReset"), ResetSettings);
            RowButton("PerceptionLayout", 298, 454, 262, Loc.T("PerceptionLayout"), OpenPerception)
                .interactable = !preview.IsActive;
            return 516;
        }

        /// <summary>设置宿主保持打开，由现有局内门禁维持暂停。</summary>
        private void OpenPerception()
        {
            if (perceptionEditor || Architecture.GetSystem<DisplayPreviewTransaction>().IsActive) return;
            settingsPanel.gameObject.SetActive(false);
            var layer = Graphic(transform, "PerceptionEditor", 0, 0, 1, 1, Shade, true);
            Stretch(layer.rectTransform);
            perceptionEditor = layer.gameObject.AddComponent<PerceptionLayoutView>();
            var camera = FromActiveSurvival ? FindObjectOfType<PrototypeController>()?.PerceptionPreviewCamera : null;
            perceptionEditor.Open(Architecture.GetSystem<SettingsSystem>().Perception, camera, ClosePerception);
        }

        private void ClosePerception()
        {
            if (!perceptionEditor) return;
            perceptionEditor.gameObject.SetActive(false);
            Destroy(perceptionEditor.gameObject);
            perceptionEditor = null;
            settingsPanel.gameObject.SetActive(true);
        }

        private void CancelDisplayPreview()
        {
            Architecture.GetSystem<DisplayPreviewTransaction>().Cancel();
            LoadDisplay();
            pageDirty = true;
        }

        private void ResetSettings()
        {
            Architecture.GetSystem<DisplayPreviewTransaction>().Cancel();
            Architecture.GetSystem<SettingsSystem>().ResetAllToDefault();
            Architecture.GetSystem<AudioSystem>().ApplyVolumes();
            var display = Architecture.GetModel<DisplaySettingsModel>();
            Architecture.GetUtility<IDisplayResolutionApplier>().Apply(display.GetCurrentResolution(), display.CurrentWindowMode.Value);
            LoadDisplay();
            pageDirty = true;
        }

        private float RenderAudio()
        {
            var audio = Architecture.GetModel<AudioSettingsModel>();
            AudioSlider("MasterVolume", "SettingsMaster", 22, audio.MasterVolume.Value, v => audio.MasterVolume.Value = v);
            AudioSlider("MusicVolume", "SettingsMusic", 125, audio.MusicVolume.Value, v => audio.MusicVolume.Value = v);
            AudioSlider("SoundVolume", "SettingsSound", 228, audio.SoundVolume.Value, v => audio.SoundVolume.Value = v);
            RowButton("ToggleMute", 16, 345, 248, Loc.T(audio.Mute.Value ? "SettingsMuted" : "SettingsMute"), () =>
            {
                audio.Mute.Value = !audio.Mute.Value;
                ApplyAudio();
                pageDirty = true;
            }, audio.Mute.Value);
            return 415;
        }

        private void AudioSlider(string name, string key, float y, float value, Action<float> set)
        {
            string label = Loc.T(key);
            var readout = LineText(name + "Value", y, label + $"   {Mathf.RoundToInt(value * 100)}%");
            var rail = Graphic(content, name, 16, y + 52, 638, 22, Line, true);
            var fill = Graphic(rail.transform, "Fill", 0, 0, 0, 0, Focus);
            Stretch(fill.rectTransform);
            var thumb = Graphic(rail.transform, "Handle", 0, 0, 15, 32, TextColor);
            thumb.rectTransform.anchorMin = thumb.rectTransform.anchorMax = new Vector2(0, .5f);
            thumb.rectTransform.pivot = Vector2.one / 2;
            thumb.rectTransform.anchoredPosition = Vector2.zero;
            var slider = rail.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = thumb.rectTransform;
            slider.targetGraphic = thumb;
            slider.navigation = new Navigation {mode = Navigation.Mode.None};
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(next =>
            {
                set(next);
                ApplyAudio();
                readout.text = label + $"   {Mathf.RoundToInt(next * 100)}%";
            });
        }

        private void ApplyAudio()
        {
            Architecture.GetSystem<AudioSystem>().ApplyVolumes();
            Architecture.GetSystem<SettingsSystem>().MarkDirty();
        }

        private float RenderControls()
        {
            float y = 18;
            var keys = Architecture.GetSystem<HotKeySystem>();
            foreach (var binding in Architecture.GetModel<HotKeyModel>().Bindings)
            {
                RowButton("Bind-" + binding.ActionId, 16, y, 670, HotKeyLabel(binding), () =>
                {
                    var result = keys.BeginRebind(binding.ActionId);
                    message = result.Error;
                    conflictAction = null;
                    pageDirty = true;
                });
                y += 66;
            }
            if (keys.IsCapturingRebind)
            {
                LineText("KeyCapture", y, Loc.T("SettingsCapture"), true, 70, true);
                y += 75;
                if (!string.IsNullOrEmpty(conflictAction))
                    RowButton("ReplaceBinding", 16, y, 281, Loc.T("SettingsReplace"), () =>
                    {
                        message = keys.ConfirmPendingBinding(true).Error;
                        conflictAction = null;
                        pageDirty = true;
                    }, true);
                RowButton("CancelBinding", 309, y, 209, Loc.T("CommonCancel"), () =>
                {
                    keys.CancelRebind();
                    conflictAction = null;
                    pageDirty = true;
                });
                y += 68;
            }
            if (!string.IsNullOrEmpty(message)) LineText("SettingsKeyError", y, message, true, 80, true);
            return y + 82;
        }

        private float RenderUpdates()
        {
            var snapshot = lastUpdate?.Snapshot ?? Architecture.GetSystem<ResourceUpdateSystem>().Snapshot;
            LineText("Version", 12, PrototypeUpdateText.CurrentVersion(snapshot));
            LineText("UpdateStatus", 60, checking ? Loc.T("UpdateChecking") : PrototypeUpdateText.Status(snapshot), true, 68);
            RowButton("CheckUpdates", 16, 141, 268, Loc.T("SettingsCheck"), () => Check().Forget()).interactable = !checking;
            float y = 216;
            if (lastUpdate is { } result)
            {
                if (snapshot.Status == ResourceUpdateStatus.UpdateAvailable && snapshot.HasKnownDownloadSize)
                    y = AddUpdateText("ResourceSize", y, Loc.T("UpdateResourceSize", snapshot.TotalDownloadCount, snapshot.TotalDownloadBytes / 1048576d));
                if (result.ApplicationUpdate is { } app)
                    y = AddUpdateText("ApplicationUpdate", y, Loc.T("UpdateApplication", Application.version, app.versionName, app.sizeBytes / 1048576d));
                if (result.Notice.HasNotice)
                    y = AddUpdateText("UpdateNotice", y, ResourceUpdateNoticeDocument.FormatForNotify(result.Notice.Content, true));
                bool canRestart = snapshot.Status == ResourceUpdateStatus.UpdateAvailable || result.ApplicationUpdate != null;
                if (canRestart && !checking)
                {
                    if (restartPending)
                    {
                        y = AddUpdateText("RestartWarning", y, Loc.T("SettingsRestartRunWarning"), true);
                        RowButton("ConfirmRestart", 16, y, 275, Loc.T("SettingsRestart"), ConfirmUpdateRestart, true);
                        RowButton("CancelRestart", 303, y, 200, Loc.T("CommonCancel"), () => { restartPending = false; pageDirty = true; });
                    }
                    else RowButton("RequestRestart", 16, y, 275, Loc.T("SettingsRestart"), RequestUpdateRestart);
                    y += 66;
                }
            }
            if (!string.IsNullOrEmpty(message)) y = AddUpdateText("UpdateError", y, message, true);
            return y;
        }

        private float AddUpdateText(string name, float y, string value, bool alert = false)
        {
            var label = LineText(name, y, value, true, alert: alert);
            float height = Mathf.Max(42, label.GetPreferredValues(value, SectionWidth - 16, 0).y + 12);
            label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            label.alignment = TextAlignmentOptions.TopLeft;
            return y + height + 8;
        }
    }
}
