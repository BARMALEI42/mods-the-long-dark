using MelonLoader;
using Il2Cpp;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace TLD_Multiplayer
{
    public class LobbySystem
    {
        private MelonPreferences_Category configCategory;
        private MelonPreferences_Entry<string> prefIP;
        private MelonPreferences_Entry<string> prefPort;

        public string serverIP => prefIP.Value;
        public string serverPort => prefPort.Value;
        public string statusText = "Ожидание...";

        public string[] regions = { "LakeRegion", "MountainTownRegion", "CoastalRegion", "RuralRegion" };
        public int selectedRegionIdx = 0;
        public string[] difficulties = { "ExperienceModePilgrim", "ExperienceModeVoyageur", "ExperienceModeStalker", "ExperienceModeInterloper" };
        public int selectedDiffIdx = 1;

        public LobbySystem()
        {
            configCategory = MelonPreferences.CreateCategory("TLD_Multiplayer");
            prefIP = configCategory.CreateEntry("ServerIP", "127.0.0.1");
            prefPort = configCategory.CreateEntry("ServerPort", "7777");
        }

        public void OnGUI()
        {
            if (!Il2Cpp.GameManager.IsMainMenuActive()) return;

            GUI.Box(new Rect(20, 20, 300, 450), "TLD MULTIPLAYER LOBBY");
            GUI.Label(new Rect(40, 50, 220, 20), "Статус: " + statusText);

            if (!MainMod.Instance.isServer && !MainMod.Instance.isClient)
            {
                if (GUI.Button(new Rect(40, 80, 220, 30), "HOST SERVER")) StartHost();
                if (GUI.Button(new Rect(40, 120, 220, 30), "CONNECT")) StartClient();
            }

            if (MainMod.Instance.isServer)
            {
                if (GUI.Button(new Rect(40, 180, 220, 30), "Region: " + regions[selectedRegionIdx]))
                {
                    selectedRegionIdx = (selectedRegionIdx + 1) % regions.Length;
                    SendLobbyUpdate();
                }

                if (GUI.Button(new Rect(40, 300, 220, 50), "START GAME")) MainMod.Instance.StartGameTogether();
            }
        }

        private void StartHost()
        {
            if (MainMod.Instance._netHandler.Manager.Start(int.Parse(serverPort)))
            {
                MainMod.Instance.isServer = true;
                statusText = "ХОСТ ЗАПУЩЕН";
            }
        }

        private void StartClient()
        {
            MainMod.Instance._netHandler.Manager.Start();
            MainMod.Instance._netHandler.Manager.Connect(serverIP, int.Parse(serverPort), "TLD_MP");
            MainMod.Instance.isClient = true;
            statusText = "ПОДКЛЮЧЕНИЕ...";
        }

        public void SendLobbyUpdate()
        {
            if (!MainMod.Instance.isServer) return;
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.LobbyUpdate);
            writer.Put(selectedRegionIdx);
            writer.Put(selectedDiffIdx);
            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendStartGame()
        {
            if (!MainMod.Instance.isServer) return;
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.StartGame);
            writer.Put(selectedRegionIdx);
            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}