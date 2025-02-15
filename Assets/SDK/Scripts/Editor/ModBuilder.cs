﻿using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using System.IO;
using System.Collections.Generic;
using System;
using UnityEditor.Android;
using Newtonsoft.Json;
using System.Linq;

namespace ThunderRoad
{
    public class ModBuilder : EditorWindow
    {
        public static string buildPath
        {
            get
            {
                string buildtarget = "Windows";
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) buildtarget = "Android";
                return (Path.Combine("BuildStaging", "AddressableAssets", buildtarget, exportFolderName));
            }
        }

        public static string catalogPath
        {
            get
            {
                return (Path.Combine("BuildStaging", "Catalog", exportFolderName));
            }
        }

        [InitializeOnLoad]
        public class Startup
        {
            static Startup()
            {
                string catalogFullPath = Path.Combine(Directory.GetCurrentDirectory(), "BuildStaging/Catalog");
                if (!Directory.Exists(catalogFullPath))
                {
                    Directory.CreateDirectory(catalogFullPath);
                    Debug.Log("Created folder " + catalogFullPath);
                }
                string buildFullPath = Path.Combine(Directory.GetCurrentDirectory(), "BuildStaging/AddressableAssets");
                if (!Directory.Exists(buildFullPath))
                {
                    Directory.CreateDirectory(buildFullPath);
                    Debug.Log("Created folder " + buildFullPath);
                }
            }
        }

        public static string projectPath;
        public static string gamePath;

        public static bool toDefault;
        public static string exportFolderName;
        public static ExportTo exportTo = ExportTo.Game;
        public static bool runGameAfterBuild;
        public static string runGameArguments;
        public static bool cleanDestination = true;

        public static Action action = Action.BuildOnly;
        public static SupportedGame gameName = SupportedGame.BladeAndSorcery;

        public delegate void BuildEvent(EventTime eventTime);
        public static event BuildEvent OnBuildEvent;

        private static bool currentCheck = true;
        private static Vector2 scrollPos;

        private static int profileIndex = 0;
        private static int previousProfileIndex = 0;
        private static string profileName = "ProfileName";

        private static List<ModBuilderProfile> profiles = new List<ModBuilderProfile>();

        public enum SupportedGame
        {
            BladeAndSorcery,
#if PrivateSDK
            FutureProjectNameHere,
#endif
        }

        public enum ExportTo
        {
            Game,
            Project,
#if PrivateSDK
            Android,
#endif
        }

        public enum Action
        {
            BuildOnly,
            ExportOnly,
            BuildAndExport,
        }

        [MenuItem("ThunderRoad (SDK)/Mod Builder")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow<ModBuilder>("Mod Builder & Exporter");
        }

        private void OnFocus()
        {
            projectPath = EditorPrefs.GetString("TRMB.ProjectPath");
            gamePath = EditorPrefs.GetString("TRMB.GamePath");
            exportFolderName = EditorPrefs.HasKey("TRMB.ExportFolderName") ? EditorPrefs.GetString("TRMB.ExportFolderName") : "MyMod";
            exportTo = (ExportTo)EditorPrefs.GetInt("TRMB.ExportTo");
            toDefault = EditorPrefs.GetBool("TRMB.ToDefault");
            runGameAfterBuild = EditorPrefs.GetBool("TRMB.RunGameAfterBuild");
            cleanDestination = EditorPrefs.GetBool("TRMB.CleanDestination");
            runGameArguments = EditorPrefs.GetString("TRMB.RunGameArguments");
            gameName = (SupportedGame)EditorPrefs.GetInt("TRMB.GameName");
            action = (Action)EditorPrefs.GetInt("TRMB.Action");

            LoadProfiles();
        }

        private void OnGUI()
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            BuildProfileGUI();

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            ExportFolderGUI();

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            if (action == Action.BuildAndExport || action == Action.BuildOnly)
            {
                GUILayout.Label(new GUIContent("Included addressable group(s)"), new GUIStyle("BoldLabel"));
                GUILayout.Space(5);

                if (GUILayout.Button("Refresh available groups") && AddressableAssetSettingsDefaultObject.Settings != null)
                {
                    foreach (AddressableAssetGroup aaGroup in GetAllInstances<AddressableAssetGroup>())
                    {
                        if (AddressableAssetSettingsDefaultObject.Settings.groups.Contains(aaGroup)) continue;
                        AddressableAssetSettingsDefaultObject.Settings.groups.Add(aaGroup);
                    }
                }

                GUILayout.Space(5);
                if (GUILayout.Button("Check/uncheck all"))
                {
                    CheckAll(currentCheck);
                    currentCheck = !currentCheck;
                }

                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(400), GUILayout.Height(300));
                if (AddressableAssetSettingsDefaultObject.Settings != null)
                {
                    foreach (AddressableAssetGroup group in AddressableAssetSettingsDefaultObject.Settings.groups)
                    {
                        if (group == null) continue;
                        BundledAssetGroupSchema bundledAssetGroupSchema = group.GetSchema<BundledAssetGroupSchema>();
                        if (bundledAssetGroupSchema != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            bool newInclude = EditorGUILayout.Toggle(bundledAssetGroupSchema.IncludeInBuild, GUILayout.MaxWidth(20));
                            if (newInclude != bundledAssetGroupSchema.IncludeInBuild)
                            {
                                bundledAssetGroupSchema.IncludeInBuild = newInclude;
                                EditorUtility.SetDirty(group);
                            }
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.ObjectField(group, typeof(AddressableAssetGroup), false, GUILayout.MaxWidth(500));
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            if (action == Action.BuildAndExport || action == Action.ExportOnly)
            {
                if (action == Action.BuildAndExport)
                {
                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                }
                ExportToGUI();
            }

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            if (GUILayout.Button(action == Action.BuildOnly ? "Build" : (action == Action.ExportOnly ? "Export" : "Build and export"))) Build(action);
        }

        private static void CheckAll(bool b)
        {
            foreach (AddressableAssetGroup group in AddressableAssetSettingsDefaultObject.Settings.groups)
            {
                BundledAssetGroupSchema bundledAssetGroupSchema = group.GetSchema<BundledAssetGroupSchema>();
                if (bundledAssetGroupSchema != null)
                {
                    group.GetSchema<BundledAssetGroupSchema>().IncludeInBuild = b;
                }
            }
        }

        public static T[] GetAllInstances<T>() where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            T[] a = new T[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                a[i] = AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return a;

        }

        static void ExportFolderGUI()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(new GUIContent(toDefault ? "Default folder name" : "Mod folder name"), new GUIStyle("BoldLabel"), GUILayout.Width(150));
            string newModeName = GUILayout.TextField(exportFolderName, 25);

            string invalidChars = new string(Path.GetInvalidFileNameChars());
            foreach (char c in invalidChars)
            {
                newModeName = newModeName.Replace(c.ToString(), "");
            }

            if (newModeName != exportFolderName)
            {
                EditorPrefs.SetString("TRMB.ExportFolderName", newModeName);
                exportFolderName = newModeName;
            }

            Action newAction = (Action)EditorGUILayout.EnumPopup("", action, GUILayout.Width(150));
            if (newAction != action)
            {
                EditorPrefs.SetInt("TRMB.Action", (int)newAction);
                action = newAction;
            }

#if PrivateSDK
            bool newToDefault = GUILayout.Toggle(toDefault, new GUIContent("Default", "Export files and set catalog bundle paths to default folder (Warpfrog devs only!)"), GUILayout.Width(80));
            if (newToDefault != toDefault)
            {
                EditorPrefs.SetBool("TRMB.ToDefault", newToDefault);
                toDefault = newToDefault;
            }
#endif

            GUILayout.EndHorizontal();
        }

        static void BuildProfileGUI()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(new GUIContent("Profile"), new GUIStyle("BoldLabel"), GUILayout.Width(150));

            previousProfileIndex = profileIndex;
            profileIndex = EditorGUILayout.Popup(profileIndex, profiles.Select(n => n.profileName).ToArray());

            if (profileIndex != previousProfileIndex)
            {
                OnProfileIndexChange();
            }

            GUILayout.EndHorizontal();


            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Save profile"))
            {
                SaveProfile();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Add profile..."))
            {
                AddProfile();
            }

            profileName = GUILayout.TextField(profileName);

            GUILayout.EndHorizontal();
        }

        static void OnProfileIndexChange()
        {
            ModBuilderProfile currentProfile = profiles[profileIndex];

            foreach (AddressableAssetGroup group in AddressableAssetSettingsDefaultObject.Settings.groups)
            {
                BundledAssetGroupSchema bundledAssetGroupSchema = group.GetSchema<BundledAssetGroupSchema>();
                if (bundledAssetGroupSchema != null)
                {

                    if (currentProfile.groups != null)
                    {
                        if (currentProfile.groups.ContainsKey(group.Name))
                        {
                            group.GetSchema<BundledAssetGroupSchema>().IncludeInBuild = currentProfile.groups[group.Name];
                        }
                        else
                        {
                            Debug.LogWarning("Group " + group.Name + " not found in profile " + currentProfile.profileName + ".");
                        }
                    }
                    else
                        CheckAll(false);
                }
            }

            exportFolderName = currentProfile.exportFolder;
        }

        static void SaveProfile()
        {
            ModBuilderProfile currentProfile = profiles[profileIndex];

            ModBuilderProfile mbp = new ModBuilderProfile();
            mbp.groups = new Dictionary<string, bool>();

            foreach (AddressableAssetGroup group in AddressableAssetSettingsDefaultObject.Settings.groups)
            {
                BundledAssetGroupSchema bundledAssetGroupSchema = group.GetSchema<BundledAssetGroupSchema>();
                if (bundledAssetGroupSchema != null)
                {
                    mbp.groups.Add(group.Name, group.GetSchema<BundledAssetGroupSchema>().IncludeInBuild);
                }
            }

            mbp.profileName = currentProfile.profileName;
            mbp.exportFolder = exportFolderName;

            profiles[profileIndex] = mbp;

            string json = JsonConvert.SerializeObject(profiles, Formatting.Indented);

            using (StreamWriter sw = new StreamWriter(Application.dataPath + "/SDK/modprofiles.json"))
            {
                sw.Write(json);
                sw.Close();
            }
        }

        static void LoadProfiles()
        {
            string json;

            using (StreamReader sr = new StreamReader(Application.dataPath + "/SDK/modprofiles.json"))
            {
                json = sr.ReadToEnd();
                sr.Close();
            }

            profiles = JsonConvert.DeserializeObject<List<ModBuilderProfile>>(json);
        }

        static void AddProfile()
        {
            if (profiles.FindIndex(x => x.profileName == profileName) != -1)
            {
                Debug.LogError("Profile " + profileName + " already exists.");
            }
            else
            {
                profiles.Add(new ModBuilderProfile(profileName, exportFolderName, null));
                profileIndex = profiles.Count - 1;
            }
        }

        static void ExportToGUI()
        {
            GUILayout.BeginHorizontal();
            ExportTo newExportTo = (ExportTo)EditorGUILayout.EnumPopup("Export to", exportTo);
            if (newExportTo != exportTo)
            {
                EditorPrefs.SetInt("TRMB.ExportTo", (int)newExportTo);
                exportTo = newExportTo;
            }

            EditorGUI.BeginDisabledGroup((exportTo == ExportTo.Project) ? true : false);
            bool newRunGameAfterBuild = GUILayout.Toggle(runGameAfterBuild, "Run game after build", GUILayout.Width(150));
            if (newRunGameAfterBuild != runGameAfterBuild)
            {
                EditorPrefs.SetBool("TRMB.RunGameAfterBuild", newRunGameAfterBuild);
                runGameAfterBuild = newRunGameAfterBuild;
            }
            EditorGUI.EndDisabledGroup();

            bool newCleanDestination = GUILayout.Toggle(cleanDestination, "Clean destination", GUILayout.Width(150));
            if (newCleanDestination != cleanDestination)
            {
                EditorPrefs.SetBool("TRMB.CleanDestination", newCleanDestination);
                cleanDestination = newCleanDestination;
            }


            GUILayout.EndHorizontal();

            if (runGameAfterBuild && exportTo == ExportTo.Game)
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Arguments"), new GUIStyle("BoldLabel"), GUILayout.Width(150));
                string newRunGameArguments = GUILayout.TextField(runGameArguments, 25);
                if (newRunGameArguments != runGameArguments)
                {
                    EditorPrefs.SetString("TRMB.RunGameArguments", newRunGameArguments);
                    runGameArguments = newRunGameArguments;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(5);
#if PrivateSDK
            if (exportTo == ExportTo.Android)
            {
                GUILayout.Space(5);
                SupportedGame newGameName = (SupportedGame)EditorGUILayout.EnumPopup("Game name", gameName);
                if (newGameName != gameName)
                {
                    EditorPrefs.SetInt("TRMB.GameName", (int)newGameName);
                    gameName = newGameName;
                }
            }
#endif
            if (exportTo == ExportTo.Game)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Game folder Directory"), new GUIStyle("BoldLabel"), GUILayout.Width(150));
                if (GUILayout.Button(gamePath, new GUIStyle("textField")))
                {
                    gamePath = EditorUtility.OpenFolderPanel("Select game folder", "", "");
                    EditorPrefs.SetString("TRMB.GamePath", gamePath);
                }
                GUILayout.EndHorizontal();
            }

            if (exportTo == ExportTo.Project)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Project folder Directory"), new GUIStyle("BoldLabel"), GUILayout.Width(150));
                if (GUILayout.Button(projectPath, new GUIStyle("textField")))
                {
                    projectPath = EditorUtility.OpenFolderPanel("Select project folder", "", "");
                    EditorPrefs.SetString("TRMB.ProjectPath", projectPath);
                }
                GUILayout.EndHorizontal();
            }
        }

        static void Build(Action behaviour)
        {
            // Check error
            if (exportTo == ExportTo.Project && !Directory.Exists(Path.Combine(projectPath, "Assets/StreamingAssets")))
            {
                Debug.LogError("Cannot deploy to project dir as the folder doesn't seem to be an Unity project");
                return;
            }
            if (exportTo == ExportTo.Game)
            {
                bool gameSupported = false;
                foreach (string supportedGame in Enum.GetNames(typeof(SupportedGame)))
                {
                    if (File.Exists(Path.Combine(gamePath, supportedGame + ".exe")))
                    {
                        gameSupported = true;
                        gameName = (SupportedGame)Enum.Parse(typeof(SupportedGame), supportedGame);
                    }
                }
                if (!gameSupported)
                {
                    Debug.LogError("Target game not supported!");
                    return;
                }
            }
#if PrivateSDK
            if (exportTo == ExportTo.Android)
            {
                string adbPath = Path.Combine(EditorPrefs.GetString("AndroidSdkRoot"), "platform-tools", "adb.exe");
                if (!EditorPrefs.HasKey("AndroidSdkRoot") || !File.Exists(adbPath))
                {
                    Debug.LogError("Android SDK is not installed!");
                    Debug.LogError("Path not found " + adbPath);
                    return;
                }
            }
#endif
            // Configure stereo rendering
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
            }
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
            {
                PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            }

            // Configure addressable groups
            if (AddressableAssetSettingsDefaultObject.Settings != null)
            {
                foreach (AddressableAssetGroup group in AddressableAssetSettingsDefaultObject.Settings.groups)
                {
                    BundledAssetGroupSchema bundledAssetGroupSchema = group.GetSchema<BundledAssetGroupSchema>();
                    if (bundledAssetGroupSchema != null)
                    {
                        if (group.Default)
                        {
                            bundledAssetGroupSchema.BuildPath.SetVariableByName(AddressableAssetSettingsDefaultObject.Settings, "LocalBuildPath");
                            bundledAssetGroupSchema.LoadPath.SetVariableByName(AddressableAssetSettingsDefaultObject.Settings, "LocalLoadPath");
                        }
                        bundledAssetGroupSchema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.NoHash;
                        bundledAssetGroupSchema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.NoHash;
                        AddressableAssetSettingsDefaultObject.Settings.profileSettings.SetValue(group.Settings.activeProfileId, "LocalBuildPath", "[ThunderRoad.ModBuilder.buildPath]");
                        AddressableAssetSettingsDefaultObject.Settings.profileSettings.SetValue(group.Settings.activeProfileId, "LocalLoadPath", (toDefault ? "{ThunderRoad.FileManager.aaDefaultPath}/" : "{ThunderRoad.FileManager.aaModPath}/") + exportFolderName);
                        // Set builtin shader to export folder name to avoid duplicates
                        AddressableAssetSettingsDefaultObject.Settings.ShaderBundleNaming = UnityEditor.AddressableAssets.Build.ShaderBundleNaming.Custom;
                        AddressableAssetSettingsDefaultObject.Settings.ShaderBundleCustomNaming = exportFolderName;
                        AddressableAssetSettingsDefaultObject.Settings.BuildRemoteCatalog = true;
                        /* TODO: OBB support (zip file uncompressed and adb push to obb folder)
                            AddressableAssetSettingsDefaultObject.Settings.profileSettings.SetValue(group.Settings.activeProfileId, "LocalLoadPath", "{ThunderRoad.FileManager.obbPath}/" + exportFolderName + "{ThunderRoad.FileManager.obbPathEnd}");
                        */
                    }
                }
            }
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();

            // Build
            if (behaviour == Action.BuildAndExport || behaviour == Action.BuildOnly)
            {
                Debug.Log("Build path is: " + buildPath);
                if (OnBuildEvent != null) OnBuildEvent.Invoke(EventTime.OnStart);

                // Clean build path
                if (Directory.Exists(buildPath))
                {
                    foreach (string filePath in Directory.GetFiles(buildPath, "*.*", SearchOption.AllDirectories)) File.Delete(filePath);
                }

                BuildCache.PurgeCache(true);
                AddressableAssetSettings.CleanPlayerContent();
                AddressableAssetSettings.CleanPlayerContent(AddressableAssetSettingsDefaultObject.Settings.ActivePlayerDataBuilder);
                AddressableAssetSettings.BuildPlayerContent();
                Debug.Log("Build done");

                if (OnBuildEvent != null) OnBuildEvent.Invoke(EventTime.OnEnd);
            }

            // Export
            if (behaviour == Action.BuildAndExport || behaviour == Action.ExportOnly)
            {
                if (exportTo == ExportTo.Game || exportTo == ExportTo.Project)
                {
                    // Get paths
                    string buildFullPath = Path.Combine(Directory.GetCurrentDirectory(), buildPath);
                    string catalogFullPath = Path.Combine(Directory.GetCurrentDirectory(), catalogPath);
                    string destinationAssetsPath = "";
                    string destinationCatalogPath = "";
                    if (exportTo == ExportTo.Project)
                    {
                        destinationAssetsPath = Path.Combine(projectPath, buildPath);
                        destinationCatalogPath = Path.Combine(projectPath, catalogPath);
                    }
                    else if (exportTo == ExportTo.Game)
                    {
                        if (toDefault) destinationAssetsPath = destinationCatalogPath = Path.Combine(gamePath, gameName + "_Data/StreamingAssets/Default", exportFolderName);
                        else destinationAssetsPath = destinationCatalogPath = Path.Combine(gamePath, gameName + "_Data/StreamingAssets/Mods", exportFolderName);
                    }

                    // Create folders if needed
                    if (!File.Exists(destinationAssetsPath)) Directory.CreateDirectory(destinationAssetsPath);
                    if (!File.Exists(destinationCatalogPath)) Directory.CreateDirectory(destinationCatalogPath);

                    // Clean destination path
                    if (cleanDestination)
                    {
                        foreach (string filePath in Directory.GetFiles(destinationAssetsPath, "*.*", SearchOption.AllDirectories)) File.Delete(filePath);
                        if (exportTo == ExportTo.Game)
                        {
                            foreach (string filePath in Directory.GetFiles(destinationCatalogPath, "*.*", SearchOption.AllDirectories)) File.Delete(filePath);
                        }
                    }
                    else
                    {
                        foreach (string filePath in Directory.GetFiles(destinationAssetsPath, "catalog_*.json", SearchOption.AllDirectories)) File.Delete(filePath);
                        foreach (string filePath in Directory.GetFiles(destinationAssetsPath, "catalog_*.hash", SearchOption.AllDirectories)) File.Delete(filePath);
                        if (exportTo == ExportTo.Game)
                        {
                            foreach (string filePath in Directory.GetFiles(destinationCatalogPath, "catalog_*.json", SearchOption.AllDirectories)) File.Delete(filePath);
                            foreach (string filePath in Directory.GetFiles(destinationCatalogPath, "catalog_*.hash", SearchOption.AllDirectories)) File.Delete(filePath);
                        }
                    }

                    // Copy addressable assets to destination path
                    CopyDirectory(buildFullPath, destinationAssetsPath);
                    Debug.Log("Copied addressable asset folder " + buildFullPath + " to " + destinationAssetsPath);

                    if (exportTo == ExportTo.Game)
                    {
                        // Copy json catalog to destination path
                        CopyDirectory(catalogFullPath, destinationCatalogPath);
                        Debug.Log("Copied catalog folder " + catalogFullPath + " to " + destinationCatalogPath);
                        // Copy plugin dll if any
                        string dllPath = Path.Combine("BuildStaging", "Plugins", exportFolderName) + "/bin/Release/netstandard2.0/" + exportFolderName + ".dll";
                        if (File.Exists(dllPath))
                        {
                            File.Copy(dllPath, destinationCatalogPath + "/" + exportFolderName + ".dll", true);
                            Debug.Log("Copied dll " + dllPath + " to " + destinationCatalogPath);
                        }
                    }
                }

                if ((exportTo == ExportTo.Game) && runGameAfterBuild)
                {
                    System.Diagnostics.Process process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = Path.Combine(gamePath, gameName + ".exe");
                    process.StartInfo.Arguments = runGameArguments;
                    process.Start();
                    Debug.Log("Start game: " + process.StartInfo.FileName + " " + process.StartInfo.Arguments);
                }
#if PrivateSDK
                if (exportTo == ExportTo.Android)
                {
                    string buildFullPath = Path.Combine(Directory.GetCurrentDirectory(), "BuildStaging", "AddressableAssets", "Android");
                    System.Diagnostics.Process process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = GetAdbPath();
                    string destinationPath = "/sdcard/Android/data/com.Warpfrog." + gameName + "/files/mods/" + exportFolderName;
                    process.StartInfo.Arguments = "push " + buildFullPath + "/. " + destinationPath;
                    // for default: obb : /sdcard/Android/obb/" + PlayerSettings.applicationIdentifier + "/main.1.com.Warpfrog.BladeAndSorcery.obb");
                    process.Start();
                    process.WaitForExit();
                    Debug.Log(GetAdbPath() + " " + process.StartInfo.Arguments);

                    if (runGameAfterBuild)
                    {
                        process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = GetAdbPath();
                        process.StartInfo.Arguments = "shell am start -n " + PlayerSettings.applicationIdentifier + "/com.unity3d.player.UnityPlayerActivity";
                        process.Start();
                        Debug.Log("Start game: " + process.StartInfo.FileName + " " + process.StartInfo.Arguments);
                    }
                }
#endif
                Debug.Log("Export done");
            }
            // The end
            System.Media.SystemSounds.Asterisk.Play();
        }

        public static string GetAdbPath()
        {
            return Path.Combine(EditorPrefs.GetString("AndroidSdkRoot"), "platform-tools", "adb.exe");
        }

        private static void CopyDirectory(string strSource, string strDestination, string searchPattern = "*.*")
        {
            if (!Directory.Exists(strDestination))
            {
                Directory.CreateDirectory(strDestination);
            }

            DirectoryInfo dirInfo = new DirectoryInfo(strSource);
            FileInfo[] files = dirInfo.GetFiles(searchPattern);
            foreach (FileInfo tempfile in files)
            {
                tempfile.CopyTo(Path.Combine(strDestination, tempfile.Name), true);
            }

            DirectoryInfo[] directories = dirInfo.GetDirectories();
            foreach (DirectoryInfo tempdir in directories)
            {
                CopyDirectory(Path.Combine(strSource, tempdir.Name), Path.Combine(strDestination, tempdir.Name), searchPattern);
            }
        }
    }

    [JsonObject]
    [Serializable]
    public class ModBuilderProfile
    {
        public ModBuilderProfile(string n = "", string f = "", Dictionary<string, bool> g = null)
        {
            profileName = n;
            exportFolder = f;
            groups = g;
        }

        public string profileName = "";
        public string exportFolder = "";
        public Dictionary<string, bool> groups = new Dictionary<string, bool>();
    }
}