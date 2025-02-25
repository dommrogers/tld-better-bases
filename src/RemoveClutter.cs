using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;
using MelonLoader.TinyJSON;
using MelonLoader.Utils;
using static Il2Cpp.Utils;
using System.Diagnostics;

namespace BetterBases
{
	public struct BreakDownSaveProxy
	{
		public string data;
	}
	internal class RemoveClutter
	{
		public static List<GameObject> itemList = new List<GameObject>();
		public static List<BreakDownDefinition> objList = null;
		public static List<BreakDown> clutterList = new List<BreakDown>();

		public static string sceneBreakDownData = null;

		public static List<string> notReallyOutdoors = new List<string>
		{
			"DamTransitionZone"
		};

		internal static void LoadBreakDownDefinitions()
		{
			objList = new List<BreakDownDefinition>();

			/* Custom definitions */
			string customDefsDir = Path.Combine(MelonEnvironment.ModsDirectory, "bb-custom-definitions");

			if (!Directory.Exists(customDefsDir))
			{
				Directory.CreateDirectory(customDefsDir);
			}

			string[] customDefFiles = Directory.GetFiles(customDefsDir, "*.json");

			for (int i = 0; i < customDefFiles.Length; i++)
			{
				string customData = File.ReadAllText(customDefFiles[i]);
				List<BreakDownDefinition> customObjs = null;

				try
				{
					customObjs = JSON.Load(customData).Make<List<BreakDownDefinition>>();

					objList.AddRange(customObjs);

					BetterBases.Log("Custom definitions " + Path.GetFileName(customDefFiles[i]) + " loaded.", true);
				}
				catch (FormatException e)
				{
					BetterBases.Log("ERROR: " + Path.GetFileName(customDefFiles[i]) + " incorrectly formatted.", true);
				}
			}

			/* Integrated Definitions */
			string[] defFiles = { "decoration", "exterior", "industrial", "interiors", "kitchen", "tech" };

            for (int i = 0; i < defFiles.Length; i++)
            {
                System.IO.StreamReader streamReader = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BetterBases.resources.definitions."+defFiles[i]+".json"));
                string data = streamReader.ReadToEnd();
                streamReader.Close();
                List<BreakDownDefinition> fileObjs = null;

				try
				{
					fileObjs = JSON.Load(data).Make<List<BreakDownDefinition>>();

					objList.AddRange(fileObjs);

					BetterBases.Log(Path.GetFileName(defFiles[i]) + " definitions loaded ");
				}
				catch (FormatException e)
				{
					BetterBases.Log("ERROR: " + Path.GetFileName(defFiles[i]) + " incorrectly formatted.");
				}
			}
		}

		internal static void LoadBreakDownData(string saveName, string sceneSaveName)
		{
			string sceneBreakDownData = BetterBases.dataMngr.Load(sceneSaveName + "_breakdowndata");

			BetterBases.Log("Preparing clutter");

			if (clutterList != null)
			{
				clutterList = new List<BreakDown>();
			}
			else
			{
				clutterList.Clear();
			}

			PatchSceneObjects();
			PatchSceneDecals();

			BetterBases.Log("Deserializing BreakDowns");
			DeserializeBreakDownData(sceneBreakDownData);
		}

		internal static void SaveBreakDownData(SlotData slotData, string sceneSaveName)
		{
			string? saveProxyData = SerializeClutterBreakDownData();
			if (!string.IsNullOrEmpty(saveProxyData))
			{
				BetterBases.dataMngr.Save(saveProxyData, sceneSaveName + "_breakdowndata");
			}
		}

		internal static string? SerializeClutterBreakDownData()
		{
			if (clutterList == null || clutterList.Count() == 0)
			{
				return null;
			}

            List<string> bdData = new List<string>();

            foreach(BreakDown bdItem in clutterList)
            {
                if (bdItem)
                {
					bdData.Add(bdItem.Serialize());
				}
			}

			return JSON.Dump(bdData);
		}

		internal static void DeserializeBreakDownData(string data)
		{
			if (data == null)
			{
				return;
			}
            
            List<string> loadedList = JSON.Load(data).Make<List<string>>();
       
            foreach(string bdSaveProxy in loadedList)
            {
                if (bdSaveProxy == null || bdSaveProxy == "" || bdSaveProxy == "{}") { continue; }
                
				ModBreakDownSaveProxy breakDownSaveData = JSON.Load(bdSaveProxy).Make<ModBreakDownSaveProxy>();
				BreakDown breakDown = BreakDown.FindBreakDownByGuid(breakDownSaveData.m_Guid);

				if (breakDown == null)
				{
					breakDown = BreakDown.FindBreakDownByPosition(breakDownSaveData);
				}

				if (breakDown)
				{
					breakDown.Deserialize(bdSaveProxy);
				}
			}
		}

		//Item preparation - searches every object that appears on the config file
		internal static void PatchSceneObjects()
		{
			if (objList == null)
			{
				return;
			}

			Stopwatch sw = new Stopwatch();
			sw.Start();

			//Get list of all root objects
			List<GameObject> rObjs = BetterBasesUtils.GetRootObjects();

			//Results container
			List<GameObject> filteredSceneObjects = new List<GameObject>();

			//Clear object list
			itemList.Clear();
			clutterList.Clear();

			int setupObjects = 0;
			List<int> processed = new List<int>();
			string filterPrefix = "obj_";

			// build filter array
			string[] filters = objList.Where(x => !string.IsNullOrEmpty(x.filter)).OrderBy(x => x.filter).Select(x => filterPrefix + x.filter.ToLowerInvariant()).ToArray();

			// find filtered scene objects
			filteredSceneObjects = GameObject.FindObjectsOfType<GameObject>(true).Where(x => filters.Any(x.name.ToLowerInvariant().StartsWith)).ToList();

			// interate over the definitions
			foreach (BreakDownDefinition obj in objList.Where(x => !string.IsNullOrEmpty(x.filter)).OrderBy(x => x.filter))
			{
				string fullFilter = filterPrefix + obj.filter.ToLowerInvariant();
				// collect objects from the existing filtered list
				GameObject[] toProcess = filteredSceneObjects.Where(x => x.name.ToLowerInvariant().StartsWith(fullFilter)).ToArray();
				if (toProcess.Length > 0)
				{
					foreach (GameObject child in toProcess)
					{
						if (processed.Contains(child.GetInstanceID()))
						{
							continue;
						}
						if (child != null && !child.name.Contains("xpzclutter") && child.GetComponent<RepairableContainer>() == null && child.transform.parent.gameObject.active)
						{
							if (child.name.ToLowerInvariant().Contains("prefab") || child.GetComponent<LODGroup>() != null)
							{
								if (PrepareGameObject(child, obj))
								{
									processed.Add(child.GetInstanceID());
									setupObjects++;
								}
							}
						}
					}
				}
			}


			//foreach (GameObject rootObj in rObjs)
			//{
			//	//Iterate over obj config list
			//	foreach (BreakDownDefinition obj in objList.Where(x => !string.IsNullOrEmpty(x.filter)).OrderBy(x => x.filter))
			//	{

			//		BetterBasesUtils.GetChildrenWithName(rootObj, "OBJ_" + obj.filter, result);

			//		BetterBases.Log($"PatchSceneObjects ForEach RO:{rootObj.name} F:{obj.filter} Count:{result.Count} ({(float)sw.ElapsedMilliseconds / 1000:N2}s)");

			//		if (result.Count > 0)
			//		{
			//			foreach (GameObject child in result)
			//			{
			//				if (processed.Contains(child.GetInstanceID()))
			//				{
			//					continue;
			//				}
			//				if (child != null && !child.name.Contains("xpzclutter") && child.GetComponent<RepairableContainer>() == null && child.transform.parent.gameObject.active)
			//				{
			//					if (PrepareGameObject(child, obj))
			//					{
			//						processed.Add(child.GetInstanceID());
			//						setupObjects++;
			//					}
			//				}
			//			}
			//		}
			//	}
			//}

			sw.Stop();
			BetterBases.Log(setupObjects + $" clutter objects setup. ({(float)sw.ElapsedMilliseconds / 1000:N2}s)");
		}

		internal static void PatchSceneDecals()
		{
			GameObject[] rObjs = BetterBasesUtils.GetRootObjects().ToArray();

			foreach (GameObject rootObj in rObjs)
			{
				MeshRenderer childRenderer = rootObj.GetComponent<MeshRenderer>();
				MeshRenderer[] allRenderers = rootObj.GetComponentsInChildren<MeshRenderer>();
				allRenderers.AddItem(childRenderer);

				foreach (MeshRenderer renderer in allRenderers)
				{
					if (renderer.gameObject.name.ToLower().Contains("decal-"))
					{
						renderer.receiveShadows = true;
						qd_Decal decal = renderer.GetComponent<qd_Decal>();
						if (decal != null && (decal.texture.name.StartsWith("FX_DebrisPaper") || decal.texture.name.StartsWith("FX_DebrisMail") || decal.texture.name.StartsWith("FX_DebriPaper")) && !decal.gameObject.name.Contains("xpzdecal") && decal.GetComponent<GearItem>() == null)
						{
							decal.gameObject.name += "_xpzdecal";

							BreakDownDefinition bdDef = new BreakDownDefinition
							{
								filter = "Paper",
								minutesToHarvest = 1f,
								sound = "Paper"
							};

							PrepareGameObject(renderer.gameObject, bdDef);

						}

						continue;
					}
				}
			}
		}

		internal static bool PrepareGameObject(GameObject gameObject, BreakDownDefinition objDef)
		{
			//BetterBases.Log($"PrepareGameObject | {gameObject.name}");
			GearItem gearItem = gameObject.GetComponent<GearItem>();
			if (gearItem != null)
			{
				return false;
			}

			Renderer renderer = Utils.GetLargestBoundsRenderer(gameObject);

			if (renderer == null)
			{
				return false;
			}

			LODGroup lodObject = gameObject.GetComponent<LODGroup>();

			if (lodObject == null)
			{
				lodObject = gameObject.GetComponentInChildren<LODGroup>();
			}

			if (lodObject != null)
			{
				gameObject = lodObject.gameObject;
			}

			//Check if it has collider, add one if it doesn't
			Collider collider = gameObject.GetComponent<Collider>();

			if (collider == null)
			{
				collider = gameObject.GetComponentInChildren<Collider>();
			}

			if (gameObject.name.StartsWith("Decal-"))
			{
				gameObject.transform.localRotation = Quaternion.identity;
				GameObject collisionObject = new GameObject("PaperDecalRemover-" + gameObject.name);
				collisionObject.transform.parent = gameObject.transform.parent;
				collisionObject.transform.position = gameObject.transform.position;

				gameObject.transform.parent = collisionObject.transform;

				gameObject = collisionObject;
			}

            if (collider == null)
            {
                Bounds bounds = renderer.bounds;

                BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.size = bounds.size;
                boxCollider.center = bounds.center - gameObject.transform.position;
			}

			if (gameObject.GetComponent<BreakDown>() == null && gameObject.GetComponentInChildren<BreakDown>() == null)
			{
				AddBreakDownComponent(gameObject, objDef);
			}
			else if (gameObject.GetComponent<BreakDown>() != null)
			{
				gameObject.GetComponent<BreakDown>().enabled = true;
				gameObject.name += "_xpzclutter";
				if (!clutterList.Contains(gameObject.GetComponent<BreakDown>()))
				{
					clutterList.Add(gameObject.GetComponent<BreakDown>());
				}
			}
			else if (gameObject.GetComponentInChildren<BreakDown>() != null)
			{
				gameObject.GetComponentInChildren<BreakDown>().enabled = true;
				gameObject.name += "_xpzclutter";
				if (!clutterList.Contains(gameObject.GetComponent<BreakDown>()))
				{
					clutterList.Add(gameObject.GetComponent<BreakDown>());
				}
			}

			MoveableObject mObject = gameObject.AddComponent<MoveableObject>();
			mObject.m_OriginalPosition = gameObject.transform.position;
			mObject.m_CurrentPosition = gameObject.transform.position;

			gameObject.transform.parent = BetterBases.staticRoot.transform;

			//Set objects to interactive layer
			BetterBasesUtils.SetLayer(gameObject, vp_Layer.InteractiveProp, false);

			if (gameObject.transform.childCount > 0)
			{
				for (int i = 0; i < gameObject.transform.childCount; i++)
				{
					if (!gameObject.transform.GetChild(i).gameObject.name.StartsWith("Decal-"))
						BetterBasesUtils.SetLayer(gameObject.transform.GetChild(i).gameObject, vp_Layer.InteractiveProp);
				}
			}
			return true;
		}

		internal static void AddBreakDownComponent(GameObject gameObject, BreakDownDefinition objDef)
		{
			BreakDown breakDown = gameObject.AddComponent<BreakDown>();

			gameObject.name += "_xpzclutter";
			if (!BreakDown.m_BreakDownObjects.Contains(breakDown))
			{
				BreakDown.m_BreakDownObjects.Add(breakDown);
			}

			if (!clutterList.Contains(breakDown))
			{
				clutterList.Add(breakDown);
			}

			//Object yields
			if (objDef.yield != null && objDef.yield.Length > 0 && Settings.options.objectYields)
			{
				List<GameObject> itemYields = new List<GameObject>();
				List<int> numYield = new List<int>();

				foreach (BreakDownYield yield in objDef.yield)
				{
					if (yield.item.Trim() != "")
					{
						GameObject yieldItem = null;
						GearItem yieldItemObj = GearItem.LoadGearItemPrefab("GEAR_" + yield.item);

						if (yieldItemObj != null)
						{
							yieldItem = yieldItemObj.gameObject;
							itemYields.Add(yieldItem);
							numYield.Add(yield.num);
						}
						else
						{
							BetterBases.Log("Yield  GEAR_" + yield.item + " couldn't be loaded.", true);
						}
					}
				}

				breakDown.m_YieldObject = itemYields.ToArray();
				breakDown.m_YieldObjectUnits = numYield.ToArray();
			}
			else
			{
				breakDown.m_YieldObject = new GameObject[0];
				breakDown.m_YieldObjectUnits = new int[0];
			}

			//Time to harvest
			if (objDef.minutesToHarvest > 0 && !Settings.options.fastBreakDown)
				breakDown.m_TimeCostHours = objDef.minutesToHarvest / 60;
			else
				breakDown.m_TimeCostHours = 1f / 60;

			//Harvest sound
			/*MetalSaw				
			  WoodSaw				
			  Outerwear			
			  MeatlSmall			
			  Generic				
			  Metal			
			  MeatlMed				
			  Cardboard			
			  WoodCedar			
			  NylonCloth			
			  Plants				
			  Paper				
			  Wood					
			  Wool					
			  Leather				
			  WoodReclaimedNoAxe	
			  WoodReclaimed		
			  Cloth				
			  MeatLarge			
			  WoodSmall			
			  WoodFir				
			  WoodAxe		*/

			if (objDef.sound.Trim() != "" && objDef.sound != null)
				breakDown.m_BreakDownAudio = "Play_Harvesting" + objDef.sound;
			else
				breakDown.m_BreakDownAudio = "Play_HarvestingGeneric";

			//Display name
			if (Settings.options.showObjectNames)
			{
				String rawName = objDef.filter.Replace("_", string.Empty);
				String[] objWords = Regex.Split(rawName, @"(?<!^)(?=[A-Z])");
				String objName = String.Join(" ", objWords);

				breakDown.m_LocalizedDisplayName = new LocalizedString() { m_LocalizationID = objName };
			}
			else
			{
				breakDown.m_LocalizedDisplayName = new LocalizedString() { m_LocalizationID = "GAMEPLAY_BreakDown" };
			}

			if (objDef.isSmallItem)
			{
				if (!PlaceableFurniture.IsSmallItem(gameObject))
				{
					BetterBases.smallItems.Add(gameObject);
				}
			}

			//Required Tools
			if (objDef.requireTool == true && Settings.options.toolsNeeded)
			{
				breakDown.m_RequiresTool = true;
			}

			if (objDef.tools != null && objDef.tools.Length > 0 && Settings.options.toolsNeeded)
			{
				Il2CppSystem.Collections.Generic.List<GameObject> itemTools = new Il2CppSystem.Collections.Generic.List<GameObject>();

				foreach (String tool in objDef.tools)
				{
					GameObject selectedTool = null;

					if (tool.ToLower() == "knife")
					{
						selectedTool = GearItem.LoadGearItemPrefab("GEAR_Knife").gameObject;
					}
					else if (tool.ToLower() == "hacksaw")
					{
						selectedTool = GearItem.LoadGearItemPrefab("GEAR_Hacksaw").gameObject;
					}
					else if (tool.ToLower() == "hatchet")
					{
						selectedTool = GearItem.LoadGearItemPrefab("GEAR_Hatchet").gameObject;
					}
					else if (tool.ToLower() == "hammer")
					{
						selectedTool = GearItem.LoadGearItemPrefab("GEAR_Hammer").gameObject;
					}

					if (selectedTool != null)
					{
						itemTools.Add(selectedTool);
					}
					else
					{
						BetterBases.Log("Tool " + tool + " couldn't be loaded or doesn't exist.", true);
					}

				}

				Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> toolsArray = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(itemTools.ToArray());

				if (toolsArray.Length > 0)
				{
					breakDown.m_UsableTools = toolsArray;
				}
				else
				{
					BetterBases.Log("Tools array is empty.");
					breakDown.m_RequiresTool = false;
					breakDown.m_UsableTools = new GameObject[0];
				}
			}
			else
			{
				breakDown.m_UsableTools = new GameObject[0];
			}

			//Set Unique GUID
			if (!gameObject.name.Contains("Decal-"))
			{
				Vector3 objPos = gameObject.transform.position;
				string guid = gameObject.name + "-" + objPos.ToString("f3");
				guid = guid.Replace(" ", String.Empty);
				BetterBasesUtils.SetGuid(gameObject, guid);
			}
		}

		internal static void GetBDObjectsList()
		{
			foreach (BreakDown breakDown in BreakDown.m_BreakDownObjects)
			{
				BetterBases.Log(breakDown.gameObject.name);
			}
		}
	}


}
