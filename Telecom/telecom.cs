using System;
using System.Collections.Generic;
using System.Linq;
using RealAntennas;
using RealAntennas.MapUI;
using System.IO;
using System.Runtime.CompilerServices;
using CommNet;
using UnityEngine.SceneManagement;
using System.Reflection;
using CommNet.Network;

namespace σκοπός {
    [KSPScenario(
      ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames |
      ScenarioCreationOptions.RemoveFromSandboxGames | ScenarioCreationOptions.RemoveFromScienceSandboxGames,
      new[] { GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT, GameScenes.EDITOR })]
    public sealed class Telecom : ScenarioModule, principia.ksp_plugin_adapter.SupervisedWindowRenderer.ISupervisor {
        public event Action LockClearing;
        public event Action WindowsDisposal;
        public event Action WindowsRendering;

        public static void Log(string message,
                               [CallerFilePath] string file = "",
                               [CallerLineNumber] int line = 0) {
            UnityEngine.Debug.Log($"[Σκοπός Telecom]: {message} ({file}:{line})");
        }

        public Telecom() {
            Log("Constructor");
            Instance = this;
            main_window_ = new MainWindow(this);
        }

        public override void OnLoad(ConfigNode node) {
            Log("OnLoad");
            base.OnLoad(node);
            network = new Network(node.GetNode("network"));
        }

        public override void OnSave(ConfigNode node) {
            Log("OnSave");
            base.OnSave(node);
            network.Serialize(node.AddNode("network"));
        }

        private void OnGUI() {
            if (KSP.UI.Screens.ApplicationLauncher.Ready && toolbar_button_ == null) {
                LoadTextureIfExists(out UnityEngine.Texture toolbar_button_texture,
                                    "skopos_telecom.png");
                toolbar_button_ =
                    KSP.UI.Screens.ApplicationLauncher.Instance.AddModApplication(
                        onTrue: () => main_window_.Show(),
                        onFalse: () => main_window_.Hide(),
                        onHover: null,
                        onHoverOut: null,
                        onEnable: null,
                        onDisable: null,
                        visibleInScenes: KSP.UI.Screens.ApplicationLauncher.AppScenes.
                            ALWAYS,
                        texture: toolbar_button_texture);
            }
            if (HighLogic.LoadedScene == GameScenes.EDITOR) {
                main_window_.Hide();
            }
            // Make sure the state of the toolbar button remains consistent with the
            // state of the window.
            if (main_window_.Shown()) {
                toolbar_button_?.SetTrue(makeCall: false);
            } else {
                toolbar_button_?.SetFalse(makeCall: false);
            }

            if (main_window_.Shown()) {
                WindowsRendering();
            } else {
                LockClearing();
            }
        }

        private void OnDisable() {
            Log("OnDisable");
            if (toolbar_button_ != null) {
                KSP.UI.Screens.ApplicationLauncher.Instance.RemoveModApplication(
                    toolbar_button_);
            }
        }

        private bool LoadTextureIfExists(out UnityEngine.Texture texture,
                                         string path) {
            string full_path =
                KSPUtil.ApplicationRootPath + Path.DirectorySeparatorChar +
                "GameData" + Path.DirectorySeparatorChar +
                "Skopos" + Path.DirectorySeparatorChar +
                path;
            if (File.Exists(full_path)) {
                var texture2d = new UnityEngine.Texture2D(2, 2);
                UnityEngine.ImageConversion.LoadImage(
                    texture2d,
                    File.ReadAllBytes(full_path));
                texture = texture2d;
                return true;
            } else {
                texture = null;
                return false;
            }
        }

        private void FixedUpdate() {
            if (HighLogic.LoadedScene != GameScenes.EDITOR) {
                // Time does not advance in the VAB, but after a revert, it is incorrectly stuck in the past.
                ut_ = Planetarium.GetUniversalTime();
            }
            network?.Refresh();
        }

        private void LateUpdate() {
            if (!main_window_.show_network) {
                return;
            }

            if (HighLogic.LoadedSceneIsFlight && !MapView.MapIsEnabled) {
                return;
            }

            var ui = CommNet.CommNetUI.Instance as RACommNetUI;
            if (ui == null) {
                return;
            }

            ///skip over first hop mode, doesn't really make sense for network display
            if (CommNetUI.Mode == CommNetUI.DisplayMode.FirstHop) {
                ui.NextMode();
            }

            HashSet<RACommNode> stations =
                (from station in network.AllGround() select station.Comm).ToHashSet();
            foreach (var station in stations) {
                ui.OverrideShownCones.Add(station);
            }

            if (CommNetUI.Mode == CommNetUI.DisplayMode.None) {
                return;
            }

            var isWideband = new Func<CommLink, bool>(x => x is RACommLink raLink && raLink.FwdAntennaRx.RFBand.name.Contains("Wideband") && raLink.RevAntennaRx.RFBand.name.Contains("Wideband"));

            if (CommNetUI.Mode == CommNetUI.DisplayMode.Network) {
                foreach (Vessel vessel in FlightGlobals.Vessels) {
                    if (vessel?.connection?.Comm is RACommNode node) {
                        if (node.RAAntennaList.Any(x => x.RFBand.name.Contains("Wideband"))) {
                            ui.OverrideShownCones.Add(node);
                        }
                    }
                }
                foreach (CommLink link in CommNet.CommNetNetwork.Instance.CommNet.Links) {
                    if (!isWideband(link)) {
                        continue;
                    }
                    if (link.a is RACommNode node_a &&
                    (node_a.ParentVessel != null || stations.Contains(node_a)) &&
                    link.b is RACommNode node_b &&
                    (node_b.ParentVessel != null || stations.Contains(node_b))) {
                        ui.OverrideShownLinks.Add(link);
                    }
                }
            } else {
                Vessel activeVessel = (Vessel)ui.GetType().GetField("vessel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
                if (activeVessel?.connection?.Comm is RACommNode node) {
                    if (node.RAAntennaList.Any(x => x.RFBand.name.Contains("Wideband"))) {
                        ui.OverrideShownCones.Add(node);
                    }
                    foreach (var link in node.Values) {
                        if (!isWideband(link)) {
                            continue;
                        }
                        if (CommNetUI.Mode == CommNetUI.DisplayMode.VesselLinks) {
                            ui.OverrideShownLinks.Add(link);
                        } else {
                            if (link.a is RACommNode node_a &&
                            (node_a.ParentVessel != null || stations.Contains(node_a)) &&
                            link.b is RACommNode node_b &&
                            (node_b.ParentVessel != null || stations.Contains(node_b))) {
                                ui.OverrideShownLinks.Add(link);
                            }
                        }
                    }
                }
            }
        }

        public static Telecom Instance { get; private set; }

        public Network network { get; private set; }
        [KSPField(isPersistant = true)]
        internal MainWindow main_window_;
        public double last_universal_time => ut_;
        [KSPField(isPersistant = true)]
        private double ut_;
        private KSP.UI.Screens.ApplicationLauncherButton toolbar_button_;
    }
}