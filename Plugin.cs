using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System.Globalization;
using System.Linq;

namespace CustomHat
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class CustomHatPlugin : BaseUnityPlugin
    {
        public const string pluginGuid = "com.metalted.zeepkist.customhat";
        public const string pluginName = "Custom Hat";
        public const string pluginVersion = "1.2";

        public static ConfigFile cfg;
        public static string pluginFolderPath;
        public ConfigEntry<string> hatPath;
        public ConfigEntry<int> rotationSpeed;
        public ConfigEntry<bool> keepOriginalHat;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin Custom Hat v1.2 is loaded!");

            Harmony harmony = new Harmony(pluginGuid);
            harmony.PatchAll();

            cfg = Config;
            hatPath = Config.Bind("Settings", "Hat File Name", "CustomHat.zeeplevel", "The name of the hat blueprint.");
            rotationSpeed = Config.Bind("Settings", "Rotation Speed", 0, "Angle of rotation each second.");
            keepOriginalHat = Config.Bind("Settings", "Show Original", false, "Should the original hat still be visible.");
            pluginFolderPath = AppDomain.CurrentDomain.BaseDirectory + @"\BepInEx\plugins";
        }
    }

    [HarmonyPatch(typeof(SetupCar), "SetupCharacter")]
    public class SetupCharacter_Patch
    {
        public static void Postfix(ref HatValues hat, ref CosmeticColor color, SetupCar __instance)
        {
            string configuredFileName = (string)CustomHatPlugin.cfg["Settings", "Hat File Name"].BoxedValue;
            try
            {
                string[] matchingFiles = Directory.GetFiles(CustomHatPlugin.pluginFolderPath, configuredFileName, SearchOption.AllDirectories);

                if (matchingFiles.Length > 0)
                {
                    CustomHatManager.ApplyCustomHat(matchingFiles[0], __instance);
                }
            }
            catch { }
        }
    }

    public static class CustomHatManager
    {
        public static List<int> forbiddenBlocks = new List<int>(new int[]{
            1462, //Ghost track
            131, //Tilt road
            160, //Bumps
            1280, 1281, 1282, //Fans
            43, 51, //Hammers
            1265, //Saw
            1269, 1270, 1271, //Rotating signs
            128, //Windmill
            393, //Helicopter
            1445 //Spooky eye
        });

        public static void ApplyCustomHat(string hatPath, SetupCar currentCar)
        {
            //Read the file.
            List<Block> fileBlocks = new List<Block>();
            try
            {
                string[] fileLines = File.ReadAllLines(hatPath);
                for(int i = 3; i < fileLines.Length; i++)
                {
                    fileBlocks.Add(new Block(fileLines[i]));
                }
            }
            catch
            {
                Debug.LogError("An error occured when trying to read the file into a block list.");
                return;
            }

            //Go over each block and try to find the spectator. All blocks will be placed relative to the spectator.
            Block spectator = null;
            foreach (Block b in fileBlocks)
            {
                if(b.blockID == 1446)
                {
                    spectator = b;
                    break;
                }
            }

            if(spectator == null)
            {
                Debug.LogError("No spectator found while trying to setup custom hat.");
                return;
            }

            //Spectator was found.
            //For testing, just instantiate the spectator at the position, so we can see the alignment and stuff.

            //Create a parent transform for the blocks
            Transform hatParent = new GameObject("Hat Parent").transform;
            //Set the hat parent at the origin
            hatParent.position = Vector3.zero;
            //Calculate the shift needed to have the spectator at the origin as well.
            Vector3 spectatorShift = spectator.position;
            //Spectator scalefactor
            float scaleFactor = 0.75f / spectator.scale.x;

            //List for storing childs
            List<Transform> childs = new List<Transform>();

            //Go over each block and instantiate them inside the parent.
            foreach(Block b in fileBlocks)
            {
                if(b.blockID == 1446) { continue; }

                if(forbiddenBlocks.Contains(b.blockID))
                {
                    continue;
                }

                BlockProperties bp = GameObject.Instantiate<BlockProperties>(PlayerManager.Instance.loader.globalBlockList.blocks[b.blockID]);
                bp.gameObject.name = PlayerManager.Instance.loader.globalBlockList.blocks[b.blockID].name;
                bp.properties = b.props;
                bp.CreateBlock();
                bp.LoadProperties();
                bp.transform.position = b.position;
                bp.transform.position -= spectatorShift;
                bp.transform.eulerAngles = b.rotation;
                bp.transform.localScale = b.scale;
                bp.transform.parent = hatParent;

                CleanGameObject(bp.gameObject);
                childs.Add(bp.gameObject.transform);
            }

            if (!(bool)CustomHatPlugin.cfg["Settings", "Show Original"].BoxedValue)
            {
                //Disable the original hat.
                MeshRenderer[] renderers = currentCar.theHat.GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer r in renderers)
                {
                    Debug.Log(r.gameObject.name);
                    r.enabled = false;
                }
            }

            //Place the hatParent at the hats position
            hatParent.parent = currentCar.theHat.transform;
            hatParent.transform.localPosition = new Vector3(1.825f, 0, 0);
            hatParent.transform.localEulerAngles = new Vector3(90,0,90);
            hatParent.transform.localScale = hatParent.transform.localScale * scaleFactor;

            //All objects are now placed correctly.
            //Unparent, unset rotation and reparent;
            foreach(Transform c in childs)
            {
                c.transform.parent = null;
            }

            hatParent.transform.localEulerAngles = Vector3.zero;

            foreach(Transform c in childs)
            {
                c.transform.parent = hatParent;
            }

            hatParent.gameObject.AddComponent<ObjectRotator>();
        }

        public static void CleanGameObject(GameObject obj)
        {
            Component[] objectComponents = obj.GetComponents(typeof(Component));
            Component[] childComponents = obj.GetComponentsInChildren(typeof(Component));
            objectComponents = objectComponents.Concat(childComponents).ToArray();

            foreach (Component c in objectComponents)
            {
                if (c == null) { continue; }

                if (c.GetType() == typeof(Rigidbody))
                {
                    ((Rigidbody)c).isKinematic = true;
                }

                bool keep = (c.GetType() == typeof(Transform)) || (c.GetType() == typeof(MeshFilter)) || (c.GetType() == typeof(MeshRenderer)) || (c.GetType() == typeof(RectTransform)) || (c.GetType() == typeof(Rigidbody));

                if (!keep)
                {
                    GameObject.Destroy(c);
                }
            }
        }
    }

    public class ObjectRotator : MonoBehaviour
    {
        int rotationValue = 0;

        void Start()
        {
            rotationValue = (int)CustomHatPlugin.cfg["Settings", "Rotation Speed"].BoxedValue;
        }
        void Update()
        {
            // Rotate the object in its local space
            transform.Rotate(new Vector3(rotationValue * Time.deltaTime,0,0), Space.Self);
        }
    }

    public class Block
    {
        public int blockID;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public List<float> props;

        public Block(string data)
        {
            string[] values = data.Split(',');
            blockID = int.Parse(values[0], CultureInfo.InvariantCulture);
            position = new Vector3(float.Parse(values[1], CultureInfo.InvariantCulture), float.Parse(values[2], CultureInfo.InvariantCulture), float.Parse(values[3], CultureInfo.InvariantCulture));
            rotation = new Vector3(float.Parse(values[4], CultureInfo.InvariantCulture), float.Parse(values[5], CultureInfo.InvariantCulture), float.Parse(values[6], CultureInfo.InvariantCulture));
            scale = new Vector3(float.Parse(values[7], CultureInfo.InvariantCulture), float.Parse(values[8], CultureInfo.InvariantCulture), float.Parse(values[9], CultureInfo.InvariantCulture));
            props = new List<float>();

            props.Add(position.x);
            props.Add(position.y);
            props.Add(position.z);
            props.Add(rotation.x);
            props.Add(rotation.y);
            props.Add(rotation.z);
            props.Add(scale.x);
            props.Add(scale.y);
            props.Add(scale.z);

            for (int i = 10; i < values.Length; i++)
            {
                props.Add(float.Parse(values[i], CultureInfo.InvariantCulture));
            }
        }
    }
}
