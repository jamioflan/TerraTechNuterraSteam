﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using UnityEngine;
using CustomModules.NuterraSteam;


namespace CustomModules.NuterraSteam.LegacyBlockLoader
{
    public static class DirectoryBlockLoader
    {
        private static Dictionary<string, DateTime> FileChanged = new Dictionary<string, DateTime>();
        private const long WatchDogTimeBreaker = 3000;
        private static DirectoryInfo m_CBDirectory;
        internal static DirectoryInfo CBDirectory
        {
            get
            {
                if (m_CBDirectory == null)
                {
                    string BlockPath = Path.Combine(NuterraMod.TTSteamDir, "Custom Blocks");
                    try
                    {
                        if (!Directory.Exists(BlockPath))
                        {
                            Directory.CreateDirectory(BlockPath);
                            // Add Block Example.json here?
                        }
                    }
                    catch (Exception E)
                    {
                        Console.WriteLine("Could not access \"" + BlockPath + "\"!");
                        throw E;
                    }
                    m_CBDirectory = new DirectoryInfo(BlockPath);
                }
                return m_CBDirectory;
            }
        }

        private static Dictionary<string, List<string>> AssetPaths = new Dictionary<string, List<string>>();
        private static Dictionary<string, Texture2D> IconStore = new Dictionary<string, Texture2D>();
        private static Dictionary<string, Mesh> MeshStore = new Dictionary<string, Mesh>();

        private static Dictionary<string, HashSet<string>> UsedPathNames = new Dictionary<string, HashSet<string>>();
        private static Dictionary<string, string> FileNameReplacements = new Dictionary<string, string>();

        private static Dictionary<int, UnofficialBlock> LegacyBlocks = new Dictionary<int, UnofficialBlock>();
        private static List<UnityEngine.Object> Assets = new List<UnityEngine.Object>();

        private static string GetRelAssetPath(string path)
        {
            string assetPath = Path.GetFullPath(path);
            string commonPath = Path.GetFullPath(CBDirectory.FullName);
            return assetPath.Replace(commonPath, "");
        }

        private static void RegisterLowLevelAssets<T>(string extension, Func<string, T> LoadFromFile, Dictionary<string, T> assetDict)
        {
            FileInfo[] assets = CBDirectory.GetFiles(extension, SearchOption.AllDirectories);
            foreach (FileInfo assetFile in assets)
            {
                string assetPath = Path.GetFullPath(assetFile.FullName);
                T asset = LoadFromFile(assetPath);

                string assetName = Path.GetFileName(assetFile.FullName);
                string relPath = GetRelAssetPath(assetPath);

                assetDict.Add(relPath, asset);
                if (!AssetPaths.ContainsKey(assetName))
                {
                    AssetPaths.Add(assetName, new List<string> { relPath });
                }
                else
                {
                    AssetPaths[assetName].Add(relPath);
                }
            }
        }

        public static void LoadAssets()
        {
            RegisterLowLevelAssets<Texture2D>("*.png", TextureFromFile, IconStore);
            RegisterLowLevelAssets<Mesh>("*.obj", MeshFromFile, MeshStore);

            // Load blocks
            FileInfo[] blocks = CBDirectory.GetFiles("*.json", SearchOption.AllDirectories);
            foreach (FileInfo block in blocks)
            {
                RegisterBlock(block);
            }

            // Resolve Assets
            ResolveAssets();
        }

        private static readonly Regex FilesRegex = new Regex(
            @":\s*" + Regex.Escape("\"") + @".+\.[a-zA-Z]+" + Regex.Escape("\""),
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        internal static string ResolveFiles(string text, string path)
        {
            string relativePath = GetRelAssetPath(path);
            MatchCollection referencedFiles = FilesRegex.Matches(text);
            Dictionary<string, string> closestFiles = new Dictionary<string, string>();

            // Take note of what files this references.
            foreach (Match file in referencedFiles)
            {
                string fileName = file.Value.Substring(1).Trim().Replace("\"", "");
                string actualFileName = Path.GetFileName(fileName);
                if (!closestFiles.ContainsKey(actualFileName))
                {
                    if (AssetPaths.TryGetValue(actualFileName, out List<string> paths))
                    {
                        // If there actually is a file by the correct name present, check which instance is closest to the block.json
                        string closest = GetClosestPath(relativePath, paths);
                        closestFiles.Add(actualFileName, closest);
                        string[] fileNameTokens = actualFileName.Split('.');

                        // Update FileNameReplacements so we know which alias to refer to the filenames by
                        if (UsedPathNames.TryGetValue(actualFileName, out HashSet<string> usedNames))
                        {
                            if (!usedNames.Contains(closest))
                            {
                                FileNameReplacements.Add(closest, fileNameTokens[0] + $"_N{usedNames.Count}." + fileNameTokens[1]);
                                usedNames.Add(closest);
                            }
                        }
                        else
                        {
                            UsedPathNames.Add(actualFileName, new HashSet<string> { closest });
                            FileNameReplacements.Add(closest, fileNameTokens[0] + $"_N0." + fileNameTokens[1]);
                        }
                    }
                }
            }

            StringBuilder sb = new StringBuilder(text);
            foreach (KeyValuePair<string, string> pair in closestFiles)
            {
                sb.Replace(pair.Key, FileNameReplacements[pair.Value]);
            }
            return sb.ToString();
        }
        private static int PathMatch(string path1, string path2)
        {
            string[] path1Tokens = path1.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            string[] path2Tokens = path2.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            int minLength = Math.Min(path1Tokens.Length, path2Tokens.Length);
            int score = 0;
            for (int i = 0; i < minLength; i++)
            {
                if (path1Tokens[i] == path2Tokens[i])
                {
                    score++;
                }
                else
                {
                    break;
                }
            }
            return score;
        }
        private static string GetClosestPath(string path, List<string> paths)
        {
            int maxMatch = 0;
            string closestPath = paths.First();
            foreach (string possiblePath in paths)
            {
                int match = PathMatch(path, possiblePath);
                if (match > maxMatch)
                {
                    match = maxMatch;
                    closestPath = possiblePath;
                }
            }
            return closestPath;
        }

        public static Mesh MeshFromFile(string path)
        {
            Mesh modelToEdit = new Mesh();
            return FastObjImporter.Instance.ImportFileFromPath(path, modelToEdit);
        }

        public static Texture2D TextureFromFile(string path)
        {
            byte[] data;
            data = File.ReadAllBytes(path);
            Texture2D texture;
            texture = new Texture2D(2, 2);
            texture.LoadImage(data);
            return texture;
        }

        public static void RegisterBlock(FileInfo blockJSON)
        {
            UnofficialBlock block = new UnofficialBlock(blockJSON);
            if (block != null)
            {
                LegacyBlocks.Add(block.ID, block);
            }
        }
        
        public static void ResolveAssets()
        {
            ModContainer container = Singleton.Manager<ManMods>.inst.FindMod("NuterraSteam");

            // Foreach asset, we get the actual value, and then rename it accordingly
            foreach (KeyValuePair<string, string> pair in FileNameReplacements)
            {
                string pathName = pair.Key;
                string assetName = pair.Value;

                if (IconStore.TryGetValue(pathName, out Texture2D icon))
                {
                    icon.name = assetName;
                    Assets.Add(icon);
                }
                else if (MeshStore.TryGetValue(pathName, out Mesh mesh))
                {
                    mesh.name = assetName;
                    Assets.Add(mesh);
                }
            }

            // Clear the stuff we're not using anymore
            AssetPaths.Clear();
            IconStore.Clear();
            MeshStore.Clear();
            UsedPathNames.Clear();
            FileNameReplacements.Clear();

            // Add all assets into the ModContainer
            container.Contents.m_AdditionalAssets.AddRange(Assets);
        }

        // this should get hooked to run right after ManMods.InjectModdedBlocks
        public static void InjectLegacyBlocks(ModSessionInfo sessionInfo)
        {
            List<string> blocksToAssign = new List<string>();
            Dictionary<string, ModdedBlockDefinition> definitionMap = new Dictionary<string, ModdedBlockDefinition>();
            foreach (KeyValuePair<int, UnofficialBlock> pair in LegacyBlocks)
            {
                if (NuterraMod.legacyToSessionIds.Keys.Contains(pair.Key))
                {
                    Console.WriteLine($"{pair.Key} has been ported to official. Using official version.");
                }
                else
                {
                    string blockID = ModUtils.CreateCompoundId("NuterraSteam", pair.Key.ToString());
                    blocksToAssign.Add(blockID);
                    definitionMap.Add(blockID, pair.Value.blockDefinition);
                }
            }

            // inject into IDs
            MethodInfo AutoAssignIDs = typeof(ManMods)
                .GetMethod(
                    "AutoAssignIDs",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(Dictionary<int, string>), typeof(List<string>), typeof(int), typeof(int) },
                    null
                );
            AutoAssignIDs.Invoke(Singleton.Manager<ManMods>.inst,
                new object[] { sessionInfo.BlockIDs, blocksToAssign, ManMods.k_FIRST_MODDED_BLOCK_ID, int.MaxValue });

            ModContainer NuterraSteamContainer = Singleton.Manager<ManMods>.inst.FindMod("NuterraSteam");

            Dictionary<int, Sprite> spriteDict = new Dictionary<int, Sprite>(16);
            Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> dictionary = new Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>>(); ;
            /* ^ Maps Corp index to block table
            {
                corp_index: {
                    corp_grade: {
                        block_id (int): ModdedBlockDefinition
                    }
                }
            }
            */

            foreach (string assignedBlock in blocksToAssign)
            {
                int blockID = sessionInfo.BlockIDs.FirstOrDefault(x => x.Value == assignedBlock).Key;
                InjectLegacyBlock(
                    sessionInfo, blockID, definitionMap[assignedBlock],
                    dictionary, spriteDict
                );
            }

            UpdateBlockUnlockTable(dictionary);
        }

        #region Injection helpers
        private static readonly FieldInfo m_BlockNames = typeof(ManMods).GetField("m_BlockNames", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo m_BlockDescriptions = typeof(ManMods).GetField("m_BlockDescriptions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo m_BlockIDReverseLookup = typeof(ManMods).GetField("m_BlockIDReverseLookup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static void InjectLegacyBlock(
            ModSessionInfo sessionInfo, int blockID, ModdedBlockDefinition moddedBlockDefinition,
            Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> dictionary,
            Dictionary<int, Sprite> spriteDict
        ) {
            ModContainer mod = Singleton.Manager<ManMods>.inst.FindMod("NuterraSteam");

            ManMods manMods = Singleton.Manager<ManMods>.inst;
            int hashCode = ItemTypeInfo.GetHashCode(ObjectTypes.Block, blockID);
            FactionSubTypes corpIndex = manMods.GetCorpIndex(moddedBlockDefinition.m_Corporation, sessionInfo);
            TankBlockTemplate physicalPrefab = moddedBlockDefinition.m_PhysicalPrefab;
            Visible visible = physicalPrefab.GetComponent<Visible>();
            if (visible == null)
            {
                d.Log("[Mods] Injected block " + moddedBlockDefinition.name + " and performed first time setup.");
                if (visible == null)
                {
                    visible = physicalPrefab.gameObject.AddComponent<Visible>();
                }
                UnityEngine.Object component = physicalPrefab.gameObject.GetComponent<Damageable>();
                ModuleDamage moduleDamage = physicalPrefab.gameObject.GetComponent<ModuleDamage>();
                if (component == null)
                {
                    physicalPrefab.gameObject.AddComponent<Damageable>();
                }
                if (moduleDamage == null)
                {
                    moduleDamage = physicalPrefab.gameObject.AddComponent<ModuleDamage>();
                }
                TankBlock component2 = physicalPrefab.gameObject.GetComponent<TankBlock>();
                component2.m_BlockCategory = moddedBlockDefinition.m_Category;
                component2.m_BlockRarity = moddedBlockDefinition.m_Rarity;
                component2.m_DefaultMass = Mathf.Clamp(moddedBlockDefinition.m_Mass, 0.0001f, float.MaxValue);
                component2.filledCells = physicalPrefab.filledCells.ToArray();
                component2.attachPoints = physicalPrefab.attachPoints.ToArray();
                visible.m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockID);
                JSONBlockLoader.Load(mod, blockID, moddedBlockDefinition, component2);
                physicalPrefab = moddedBlockDefinition.m_PhysicalPrefab;
                physicalPrefab.gameObject.SetActive(false);
                Damageable component3 = physicalPrefab.GetComponent<Damageable>();
                moduleDamage = physicalPrefab.GetComponent<ModuleDamage>();
                component2 = physicalPrefab.GetComponent<TankBlock>();
                visible = physicalPrefab.GetComponent<Visible>();
                visible.m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockID);
                component3.m_DamageableType = moddedBlockDefinition.m_DamageableType;
                moduleDamage.maxHealth = moddedBlockDefinition.m_MaxHealth;
                moduleDamage.deathExplosion = manMods.m_DefaultBlockExplosion;
                foreach (MeshRenderer meshRenderer in physicalPrefab.GetComponentsInChildren<MeshRenderer>())
                {
                    MeshRendererTemplate component4 = meshRenderer.GetComponent<MeshRendererTemplate>();
                    if (component4 != null)
                    {
                        meshRenderer.sharedMaterial = manMods.GetMaterial((int)corpIndex, component4.slot);
                        d.Assert(meshRenderer.sharedMaterial != null, "[Mods] Custom block " + moddedBlockDefinition.m_BlockDisplayName + " could not load texture. Corp was " + moddedBlockDefinition.m_Corporation);
                    }
                }
                physicalPrefab.gameObject.name = moddedBlockDefinition.name;
                physicalPrefab.gameObject.tag = "Untagged";
                physicalPrefab.gameObject.layer = LayerMask.NameToLayer("Tank");
                MeshCollider[] componentsInChildren2 = component2.GetComponentsInChildren<MeshCollider>();
                for (int i = 0; i < componentsInChildren2.Length; i++)
                {
                    componentsInChildren2[i].convex = true;
                }
                component2.transform.CreatePool(8);
            }
            else
            {
                physicalPrefab.gameObject.GetComponent<Visible>().m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockID);
                physicalPrefab.transform.CreatePool(8);
            }

            Dictionary<int, string> names = (Dictionary<int, string>) m_BlockNames.GetValue(manMods);
            names.Add(blockID, moddedBlockDefinition.m_BlockDisplayName);

            Dictionary<int, string> descriptions = (Dictionary<int, string>) m_BlockDescriptions.GetValue(manMods);
            descriptions.Add(blockID, moddedBlockDefinition.m_BlockDescription);

            Dictionary<string, int> blockIDReverseLookup = (Dictionary<string, int>) m_BlockIDReverseLookup.GetValue(manMods);
            blockIDReverseLookup.Add(moddedBlockDefinition.name, blockID);

            Singleton.Manager<ManSpawn>.inst.AddBlockToDictionary(physicalPrefab.gameObject, blockID);
            Singleton.Manager<ManSpawn>.inst.VisibleTypeInfo.SetDescriptor<FactionSubTypes>(hashCode, corpIndex);
            Singleton.Manager<ManSpawn>.inst.VisibleTypeInfo.SetDescriptor<BlockCategories>(hashCode, moddedBlockDefinition.m_Category);
            Singleton.Manager<ManSpawn>.inst.VisibleTypeInfo.SetDescriptor<BlockRarity>(hashCode, moddedBlockDefinition.m_Rarity);
            Singleton.Manager<RecipeManager>.inst.RegisterCustomBlockRecipe(blockID, moddedBlockDefinition.m_Price);
            if (moddedBlockDefinition.m_Icon != null)
            {
                spriteDict[blockID] = Sprite.Create(moddedBlockDefinition.m_Icon, new Rect(0f, 0f, (float)moddedBlockDefinition.m_Icon.width, (float)moddedBlockDefinition.m_Icon.height), Vector2.zero);
            }
            else
            {
                d.LogError(string.Format("Block {0} with ID {1} failed to inject because icon was not set", moddedBlockDefinition.name, blockID));
            }
            if (!dictionary.ContainsKey((int)corpIndex))
            {
                dictionary[(int)corpIndex] = new Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>();
            }
            Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>> dictionary3 = dictionary[(int)corpIndex];
            if (!dictionary3.ContainsKey(moddedBlockDefinition.m_Grade - 1))
            {
                dictionary3[moddedBlockDefinition.m_Grade - 1] = new Dictionary<BlockTypes, ModdedBlockDefinition>();
            }
            dictionary3[moddedBlockDefinition.m_Grade - 1].Add((BlockTypes)blockID, moddedBlockDefinition);
            JSONBlockLoader.Inject(blockID, moddedBlockDefinition);
            d.Log(string.Format("[Mods] Injected block {0} at ID {1}", moddedBlockDefinition.name, blockID));
        }

        private static readonly FieldInfo m_CurrentSession = typeof(ManMods).GetField("m_CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static void UpdateBlockUnlockTable(Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> dictionary)
        {
            BlockUnlockTable blockUnlockTable = Singleton.Manager<ManLicenses>.inst.GetBlockUnlockTable();
            ManMods manMods = Singleton.Manager<ManMods>.inst;

            foreach (KeyValuePair<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> keyValuePair2 in dictionary)
            {
                foreach (KeyValuePair<int, Dictionary<BlockTypes, ModdedBlockDefinition>> keyValuePair3 in keyValuePair2.Value)
                {
                    blockUnlockTable.AddModdedBlocks(keyValuePair2.Key, keyValuePair3.Key, keyValuePair3.Value);
                    if (manMods.IsModdedCorp((FactionSubTypes)keyValuePair2.Key))
                    {
                        ModdedCorpDefinition corpDefinition = manMods.GetCorpDefinition((FactionSubTypes)keyValuePair2.Key, (ModSessionInfo) m_CurrentSession.GetValue(manMods));
                        if (corpDefinition.m_RewardCorp != null)
                        {
                            Singleton.Manager<ManLicenses>.inst.GetRewardPoolTable().AddModdedBlockRewards(keyValuePair3.Value, keyValuePair3.Key, manMods.GetCorpIndex(corpDefinition.m_RewardCorp, null));
                        }
                    }
                    else
                    {
                        Singleton.Manager<ManLicenses>.inst.GetRewardPoolTable().AddModdedBlockRewards(keyValuePair3.Value, keyValuePair3.Key, (FactionSubTypes)keyValuePair2.Key);
                    }
                }
            }
        }
        #endregion
    }
}