using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using Photon.Pun;
using Photon.Realtime;
using Photon.Realtime.Demo;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;
using Zorro.UI.Modal;

namespace PeakGaming;



[HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyEnter")]
public class OnLobbyEnter_Patch
{
 static bool Prefix(SteamLobbyHandler __instance, LobbyEnter_t param)
    {
        FieldInfo m_isHostingField = typeof(SteamLobbyHandler).GetField("m_isHosting", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo m_currentLobby = typeof(SteamLobbyHandler).GetField("m_currentLobby", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo m_tryingToFetchLobbyDataAttempts = typeof(SteamLobbyHandler).GetField("tryingToFetchLobbyDataAttempts", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo m_currentlyWaitingForRoomID = typeof(SteamLobbyHandler).GetField("m_currentlyWaitingForRoomID", BindingFlags.NonPublic | BindingFlags.Instance);

        MethodInfo LeaveLobbyMethod = typeof(SteamLobbyHandler).GetMethod("LeaveLobby", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        MethodInfo JoinLobbyMethod = typeof(SteamLobbyHandler).GetMethod("JoinLobby", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);


        bool m_isHosting = (bool)m_isHostingField.GetValue(__instance);

        if (m_isHosting)
        {
            m_isHostingField.SetValue(__instance, false);
            return false;
        }
        if (param.m_EChatRoomEnterResponse != 1U)
        {
            Plugin.ShowNotification("Trying to join ...");
        }

        m_currentLobby.SetValue(__instance, new CSteamID(param.m_ulSteamIDLobby));

        string lobbyData = SteamMatchmaking.GetLobbyData((CSteamID)m_currentLobby.GetValue(__instance), "PhotonRegion");
        string lobbyData2 = SteamMatchmaking.GetLobbyData((CSteamID)m_currentLobby.GetValue(__instance), "CurrentScene");


        if (!string.IsNullOrEmpty(lobbyData))
        {
            m_tryingToFetchLobbyDataAttempts.SetValue(__instance, Optionable<int>.None);
            m_currentlyWaitingForRoomID.SetValue(__instance, Optionable<ValueTuple<CSteamID, string, string>>.Some(new ValueTuple<CSteamID, string, string>((CSteamID)m_currentLobby.GetValue(__instance), lobbyData2, lobbyData)));
            return false;
        }
        if (((Optionable<int>)m_tryingToFetchLobbyDataAttempts.GetValue(__instance)).IsNone)
        {
            m_tryingToFetchLobbyDataAttempts.SetValue(__instance, Optionable<int>.Some(1));
        }
        else
        {
            m_tryingToFetchLobbyDataAttempts.SetValue(__instance, Optionable<int>.Some(((Optionable<int>)m_tryingToFetchLobbyDataAttempts.GetValue(__instance)).Value + 1));
            Plugin.ShowNotification("Trying to fetch lobby data again ..." + ((Optionable<int>)m_tryingToFetchLobbyDataAttempts.GetValue(__instance)).Value);
        }
        if (((Optionable<int>)m_tryingToFetchLobbyDataAttempts.GetValue(__instance)).Value < 5)
        {
            LeaveLobbyMethod.Invoke(__instance, null);
            JoinLobbyMethod.Invoke(__instance, new object[] { (CSteamID) param.m_ulSteamIDLobby });
            return false;
        }
        LeaveLobbyMethod.Invoke(__instance, null);
        Modal.OpenModal(new DefaultHeaderModalOption("Joining failed", "Sadly my shit didn't work, something went wrong, unlucky!"), new ModalButtonsOption(new ModalButtonsOption.Option[]
        {
            new ModalButtonsOption.Option("God dammit", null)
        }), null);

        return false;
    }
}



[HarmonyPatch(typeof(LoadBalancingClient), "NickName", MethodType.Getter)]
public class NickNameGetterPatch
{
    static bool Prefix(ref string __result, LoadBalancingClient __instance)
    {
        __result = Plugin.yourName;
        return false;
    }
}

[HarmonyPatch(typeof(LoadBalancingClient), "NickName", MethodType.Setter)]
public class NickNameSetterPatch
{
    static bool Prefix(string value, LoadBalancingClient __instance)
    {
        if(__instance.LocalPlayer == null) return false;
        __instance.LocalPlayer.NickName = Plugin.yourName;
        return false;
    }
}


[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private bool showGUI = false;
    private bool slidingIn = false;

    private Rect targetRect;
    private Rect currentRect;

    private bool styleInitialized = false;
    public static string notificationMessage = "";
    public static float notificationStartTime = -1f;
    private float notificationDuration = 2f;

    // Players
    Dictionary<string, Character> playerDict = new Dictionary<string, Character>();
    private string localPlayerName = "";
    private UnityEngine.Vector2 playerScrollPos;
    private int selectedPlayerIndex;

    // Tabs
    private int currentTab = 0;
    private string[] tabNames = new string[] { "Main", "Player", "Self" };

    // self player stuff
    private bool isInfiniteStaminaEnabled = false;

    // Items
    private string[] items;
    private UnityEngine.Vector2 itemScrollPos;
    private int selectedItemIndex;

    private SteamLobbyHandler steamLobbyHandler;

    // Your name in game
    public static string yourName = "";
    
    private string filePath = "";

    private string steamId= "";
    private bool isCursorOn = false;

    private void Awake()
    {

        filePath = Path.Combine(Paths.GameRootPath, "username.txt");
        if (File.Exists(filePath))
        {
            yourName = File.ReadAllText(filePath);
        }
        else
        {
            string defaultContent = "New user";
            File.WriteAllText(filePath, defaultContent);
        }
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        harmony.PatchAll();


    }

    private void Start()
    {
        float w = Screen.width * 0.25f;
        float h = Screen.height * 0.8f;

        // Start offscreen
        currentRect = new Rect(Screen.width + 10f, 10f, w, h);
        targetRect = new Rect(Screen.width - w - 10f, 10f, w, h);

        items = ItemDatabase.GetAllObjectNames();
        ToggleGUI();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            AcquireServices();
            ToggleGUI();
        }

        float speed = 10f;
        currentRect.x = Mathf.Lerp(currentRect.x, targetRect.x, Time.deltaTime * speed);
        currentRect.y = Mathf.Lerp(currentRect.y, targetRect.y, Time.deltaTime * speed);
        currentRect.width = Mathf.Lerp(currentRect.width, targetRect.width, Time.deltaTime * speed);
        currentRect.height = Mathf.Lerp(currentRect.height, targetRect.height, Time.deltaTime * speed);

        if (Mathf.Abs(currentRect.x - targetRect.x) < 1f)
        {
            currentRect.x = targetRect.x;
            slidingIn = false;
        }
    }

    private void LateUpdate()
    {
        if(Input.GetKeyDown(KeyCode.F2))
        {
            isCursorOn = !isCursorOn;
        }

        if (isCursorOn)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }


    private void AcquireServices()
    {
        steamLobbyHandler = GameHandler.GetService<SteamLobbyHandler>();
    }

    private void ToggleGUI()
    {
        float w = Screen.width * 0.25f;
        float h = Screen.height * 0.8f;

        showGUI = !showGUI;
        Cursor.visible = showGUI;
        Cursor.lockState = showGUI ? CursorLockMode.None : CursorLockMode.Locked;

        targetRect = showGUI
            ? new Rect(Screen.width - w - 10f, 10f, w, h)
            : new Rect(Screen.width + 10f, 10f, w, h);

        slidingIn = true;
    }

    private void OnGUI()
    {
        if (!styleInitialized)
        {
            float hue = Mathf.Repeat(Time.time * 0.1f, 1f);
            Color rainbowColor = Color.HSVToRGB(hue, 1f, 1f);
            Color prevColor = GUI.color;
            GUI.color = rainbowColor;
        }

        if (showGUI || slidingIn)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);

            GUI.Window(123456, currentRect, DrawGUIWindow, "Peak multi-tool");

            GUI.color = previousColor;
        }
        if (!string.IsNullOrEmpty(notificationMessage))
        {
            float elapsed = Time.time - notificationStartTime;
            float alpha = Mathf.Clamp01(1f - (elapsed / notificationDuration));

            if (alpha > 0f)
            {
                Color prevColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);

                GUIStyle style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 14;
                style.normal.textColor = Color.white;
                style.wordWrap = true;
                style.padding = new RectOffset(10, 10, 5, 5);

                // Measure the size of the content
                UnityEngine.Vector2 contentSize = style.CalcSize(new GUIContent(notificationMessage));

                // Limit width and adjust height if wrapping occurs
                float maxWidth = 400f;
                if (contentSize.x > maxWidth)
                {
                    contentSize.x = maxWidth;
                    contentSize.y = style.CalcHeight(new GUIContent(notificationMessage), maxWidth);
                }

                float x = 5f;
                float y = Screen.height / 2f - contentSize.y / 2f;

                GUI.Box(new Rect(x, y, contentSize.x, contentSize.y + 100), notificationMessage, style);

                GUI.color = prevColor;
            }
            else
            {
                notificationMessage = "";
            }
        }
    }

    void DrawGUIWindow(int windowID)
    {
        GUILayout.Space(10);
        currentTab = GUILayout.Toolbar(currentTab, tabNames);
        GUILayout.Space(10);

        switch (currentTab)
        {
            case 0:
                DrawMainTab();
                break;
            case 1:
                DrawPlayerTab();
                break;
            case 2:
                DrawSelfTab();
                break;
        }
    }

    void DrawMainTab()
    {
        CreateAcquirePlayersButton();
        CreateStateOfGameButtons();
        CreateStartGameOnHardestButtons();
        CreateJoinLobby();
    }

    void DrawPlayerTab()
    {
        CreatePlayersVerticalSelect();
        CreateKillPlayerButton();
        CreateRespawnButtons();
        CreateWarpButtons();
        CreateItemsVerticalSelect();
        CreateFeedItemButton();
        CreateSpawnItemButtons();
        CreateSlipJellyButton();
    }

    void DrawSelfTab()
    {
        CreateInfiniteStaminaButton();
        CreateSetUsername();
    }


    private void CreateJoinLobby()
    {
        GUILayout.Label("Lobby ID");
        steamId = GUILayout.TextField(steamId);
        if (GUILayout.Button("Join Lobby"))
        {
            CSteamID lobbyId = new CSteamID(ulong.Parse(steamId));
            steamLobbyHandler.TryJoinLobby(lobbyId);
        }
    }

    private void CreateSetUsername()
    {
        GUILayout.Label("Username");
        localPlayerName = GUILayout.TextField(localPlayerName);
        if(GUILayout.Button("Save username"))
        {
            File.WriteAllText(filePath, localPlayerName);
            PhotonNetwork.NetworkingClient.LocalPlayer.NickName = localPlayerName;
        }
    }

    private void CreateStartGameOnHardestButtons()
    {
        if(GUILayout.Button("Sprint speed x100"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }

            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);

           localPlayer.refs.movement.sprintMultiplier = 100f;
            
        }
        
    }

    private void CreateSlipJellyButton()
    {
        if (GUILayout.Button("Spawn SlipperyJellyfish on everyone"))
        {
            if(PlayerHandler.GetAllPlayerCharacters().Count == 0) return;
            foreach(Character charac in PlayerHandler.GetAllPlayerCharacters())
            {
                SpawnNetworkedJellyfish(charac);
            }
        }
    }

    void SpawnNetworkedJellyfish(Character charac)
    {
        // Create root GameObject with PhotonView and TriggerRelay
        GameObject root = new GameObject("JellyfishRoot");

        PhotonView pv = root.AddComponent<PhotonView>();
        pv.ViewID = GenerateFakePhotonViewID();
        pv.TransferOwnership(charac.photonView.ViewID);

        TriggerRelay relay = root.AddComponent<TriggerRelay>();

        GameObject child = new GameObject("SlipperyJellyfish");
        child.transform.SetParent(root.transform);

        var jelly = child.AddComponent<SlipperyJellyfish>();

        var col = child.AddComponent<SphereCollider>();
        col.isTrigger = true;

        root.transform.position = charac.refs.head.transform.position;

        pv.RPC("RPCA_TriggerWithTarget", RpcTarget.All, new object[]
        {
            base.transform.GetSiblingIndex(),
            charac.photonView.ViewID
        });

    }

    // ⚠️ PhotonView IDs are normally assigned by Photon — this is a placeholder
    int GenerateFakePhotonViewID()
    {
        // WARNING: This is not safe in real multiplayer. Use actual PhotonNetwork.Instantiate() if possible.
        return UnityEngine.Random.Range(10000, 99999);
    }


    private void CreateSpawnItemButtons()
    {
        if (GUILayout.Button("Spawn item on player"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            PhotonNetwork.Instantiate("0_Items/" + items[selectedItemIndex], localPlayer.Center + UnityEngine.Vector3.up * 3f, UnityEngine.Quaternion.identity, 0, null).GetComponent<Item>().Interact(localPlayer);
        }

        if (GUILayout.Button("Spawn item on everyone"))
        {
            foreach (Character charac in playerDict.Values)
            {
                PhotonNetwork.Instantiate("0_Items/" + items[selectedItemIndex], charac.Center + UnityEngine.Vector3.up * 3f, UnityEngine.Quaternion.identity, 0, null).GetComponent<Item>().Interact(charac);
            }
        }
    }

    void CreateStateOfGameButtons()
    {
        if (GUILayout.Button("Fail game"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            localPlayer.photonView.RPC("RPCEndGame", RpcTarget.All, Array.Empty<object>());
        }
        if(GUILayout.Button("Win game"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            GlobalEvents.TriggerSomeoneWonRun();
        }

    }

    void CreateAcquirePlayersButton()
    {
        if (GUILayout.Button("Acquire players"))
        {
            playerDict.Clear();
            foreach(Character charac in PlayerHandler.GetAllPlayerCharacters()){
                playerDict.Add(charac.player.photonView.Owner.NickName, charac);
                if (charac.IsLocal)
                {
                    localPlayerName = charac.player.photonView.Owner.NickName;
                }
            }

            if(playerDict.Count > 0){
                ShowNotification("Acquired " + playerDict.Count + " players");
            }
            else
            {
                ShowNotification("No players found");
            }
            
        }
    }

    void CreateFeedItemButton()
    {
        if(GUILayout.Button("Feed item from hand"))
        {
            Character localCharacter = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            Character selectedCharacter = playerDict.Values.First(x => x.player.photonView.Owner.NickName == playerDict.Keys.ToArray()[selectedPlayerIndex]);
            PhotonNetwork.Instantiate("0_Items/" + items[selectedItemIndex], localCharacter.Center + UnityEngine.Vector3.up * 3f, UnityEngine.Quaternion.identity, 0, null).GetComponent<Item>().Interact(localCharacter);
            selectedCharacter.photonView.RPC("GetFedItemRPC", RpcTarget.All, new object[] { localCharacter.data.currentItem.photonView.ViewID });
        }
    }

    void CreateRespawnButtons()
    {
        if(GUILayout.Button("Respawn self at player"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            Character selectedPlayer = playerDict[playerDict.Keys.ToArray()[selectedPlayerIndex]];
            localPlayer.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, new object[]
                {
                    selectedPlayer.refs.head.transform.position,
                    true
                });
        }
        if(GUILayout.Button("Respawn player at self"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            Character selectedPlayer = playerDict[playerDict.Keys.ToArray()[selectedPlayerIndex]];
            selectedPlayer.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, new object[]
                {
                    localPlayer.refs.head.transform.position,
                    true
                });
        }
    }

    void CreateWarpButtons()
    {
        if(GUILayout.Button("Warp self to player"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            Character selectedPlayer = playerDict[playerDict.Keys.ToArray()[selectedPlayerIndex]];
            localPlayer.photonView.RPC("WarpPlayerRPC", RpcTarget.All, new object[] { selectedPlayer.refs.head.transform.position, false });
        }

        if (GUILayout.Button("Warp player to self"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            Character selectedPlayer = playerDict[playerDict.Keys.ToArray()[selectedPlayerIndex]];
            selectedPlayer.photonView.RPC("WarpPlayerRPC", RpcTarget.All, new object[] { localPlayer.refs.head.transform.position, false });
        }

        if(GUILayout.Button("Warp everyone to self"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character localPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
            foreach(Character character in playerDict.Values)
            {
                character.photonView.RPC("WarpPlayerRPC", RpcTarget.All, new object[] { localPlayer.refs.head.transform.position, false });
            }
        }
    }

    void CreateInfiniteStaminaButton()
    {
        if (GUILayout.Button("Infinite stamina" + (isInfiniteStaminaEnabled ? " ON" : " OFF")))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            isInfiniteStaminaEnabled = !isInfiniteStaminaEnabled;
            if(isInfiniteStaminaEnabled) StartCoroutine(InfiniteStaminaEnumerator());
            ShowNotification("Infinite stamina " + (isInfiniteStaminaEnabled ? "ON" : "OFF"));
        }
    }

    IEnumerator InfiniteStaminaEnumerator()
    {
        Character selfPlayer = playerDict.Values.First(x => x.player.photonView.Owner.NickName == localPlayerName);
        while (isInfiniteStaminaEnabled)
        {
            selfPlayer.AddStamina(100f);
            selfPlayer.AddExtraStamina(100f);
            yield return new WaitForSeconds(0.05f);
        }
    }

    void CreatePlayersVerticalSelect()
    {
        if (playerDict.Count == 0)
        {
            return;
        }
        GUILayout.BeginVertical("box", GUILayout.Width(200));
        GUILayout.Label("Players:");
        playerScrollPos = GUILayout.BeginScrollView(playerScrollPos, GUILayout.Height(150), GUILayout.Width(200));
        selectedPlayerIndex = GUILayout.SelectionGrid(selectedPlayerIndex, playerDict.Keys.ToArray(), 1);
        GUILayout.EndScrollView();
        GUILayout.Label("Selected: " + playerDict.Keys.ToArray()[selectedPlayerIndex]);
        GUILayout.EndVertical();
    }

    void CreateItemsVerticalSelect()
    {
        GUILayout.BeginVertical("box", GUILayout.Width(200));
        GUILayout.Label("Items:");
        itemScrollPos = GUILayout.BeginScrollView(itemScrollPos, GUILayout.Height(150), GUILayout.Width(200));
        selectedItemIndex = GUILayout.SelectionGrid(selectedItemIndex, items, 1);
        GUILayout.EndScrollView();
        GUILayout.Label("Selected: " + items[selectedItemIndex]);
        GUILayout.EndVertical();
    }

    void CreateKillPlayerButton()
    {
        if (GUILayout.Button("Kill player"))
        {
            if (playerDict.Count == 0)
            {
                ShowNotification("Aqcuire players first");
                return;
            }
            Character selectedCharacter = playerDict.Values.First(x => x.player.photonView.Owner.NickName == playerDict.Keys.ToArray()[selectedPlayerIndex]);
            selectedCharacter.photonView.RPC("RPCA_Die", RpcTarget.All, new object[] { selectedCharacter.Center });
            ShowNotification("Killed player " + playerDict.Keys.ToArray()[selectedPlayerIndex]);
        }
        
    }

    public static void ShowNotification(string message)
    {
        notificationMessage = message;
        notificationStartTime = Time.time;
    }
}