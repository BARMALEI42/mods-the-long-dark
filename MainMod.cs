using MelonLoader;
using UnityEngine;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using LiteNetLib;
using LiteNetLib.Utils;
using Il2Cpp;

[assembly: MelonInfo(typeof(TLD_Multiplayer.MainMod), "TLD Multiplayer", "1.2.0", "Barabenze")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
namespace TLD_Multiplayer
{
    public class MainMod : MelonMod
    {
        public static MainMod Instance;
        public NetworkHandler _netHandler;
        public LobbySystem _lobby;
        public Dictionary<int, RemotePlayer> Players = new Dictionary<int, RemotePlayer>();
        public bool isServer, isClient;
        public int SleepingPlayers = 0;
        private float worldSyncTimer = 0f;
        public override void OnInitializeMelon()
        {
            Instance = this;
            ClassInjector.RegisterTypeInIl2Cpp<RemotePlayer>();
            _netHandler = new NetworkHandler(this);
            _lobby = new LobbySystem();
            new HarmonyLib.Harmony("com.tld.multiplayer").PatchAll();
        }

        private float time_bank = 0.0f;
        private float wait_bank = 0.1f;
        private float visualTime = 0.0f;

        public override void OnUpdate()
        {
            _netHandler.Manager.PollEvents();
            if (Il2Cpp.GameManager.IsMainMenuActive() != true)  {
                if (Il2Cpp.GameManager.GetPlayerObject() != null) { 
                    time_bank += Time.deltaTime;
                    if (time_bank > wait_bank)
                    {
                        visualTime = time_bank;

                        // Remove the recorded 0.05 seconds.
                        time_bank = time_bank - wait_bank;
                        UpdateWorldLogic();
                        SendLocalPlayerSync();
                    }
                }
            }
            
        }

        private void UpdateWorldLogic()
        {
            var tod = Il2Cpp.TimeOfDay.Instance;
            if (tod == null) return;

            // Считаем всех: ты + друзья
            int totalPlayers = Players.Count + 1;

            // Если ВСЕ спят или ждут — ускоряем время (в 40 раз)
            if (SleepingPlayers >= totalPlayers && totalPlayers > 0)
            {
                tod.Accelerate(Time.deltaTime, 40f, false);
            }

            // Хост шлет время раз в 10 сек для синхронизации погоды
            if (isServer)
            {
                worldSyncTimer += Time.deltaTime;
                if (worldSyncTimer >= 10f)
                {
                    worldSyncTimer = 0f;
                    SendWorldSync(tod);
                }
            }
        }

        private void SendWorldSync(TimeOfDay tod)
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.WorldSync);
            writer.Put(tod.GetNormalizedTime());
            writer.Put((int)Il2Cpp.GameManager.GetWeatherComponent().m_CurrentWeatherStage);
            _netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendSleepVote(bool state)
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.SleepVote);
            writer.Put(state);
            _netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }

        private void SendLocalPlayerSync()
        {
            if (Il2Cpp.GameManager.GetPlayerObject() == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.PlayerSync);

            // ВАЖНО: Добавляем 0 как заглушку ID. 
            // Это добавит 4 байта, и HandlePlayerSync поймет, что данных достаточно.
            writer.Put(0);

            var t = Il2Cpp.GameManager.GetPlayerTransform();
            var b = Il2Cpp.GameManager.GetPlayerManagerComponent();

            writer.Put(t.position.x);
            writer.Put(t.position.y);
            writer.Put(t.position.z);
            writer.Put(t.eulerAngles.y);
            writer.Put(b.PlayerIsCrouched());

            if (isServer) _netHandler.Manager.SendToAll(writer, DeliveryMethod.Unreliable);
            else if (isClient) _netHandler.Manager.FirstPeer?.Send(writer, DeliveryMethod.Unreliable);
        }
        // Создаем универсальный метод для отправки событий взаимодействия
        public void SendMapEvent(Vector3 position, string eventType, string extraData = "")
        {
            // Зачем: Чтобы не писать для каждого чиха новый метод. 
            // Мы шлем: ГДЕ это случилось, ЧТО случилось и дополнительную инфу (например, имя предмета).

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ObjectDestroy); // Или создадим новый тип MapEvent
            writer.Put(position.x);
            writer.Put(position.y);
            writer.Put(position.z);
            writer.Put(eventType);
            writer.Put(extraData);

            _netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
        public void SyncRemotePlayer(int id, Vector3 pos, float rot, bool Crouch)
        {
            if (Il2Cpp.GameManager.IsMainMenuActive()) return;

            if (!Players.ContainsKey(id) || Players[id] == null) // Добавили проверку на null
            {
                if (Players.ContainsKey(id)) Players.Remove(id); // Чистим старую запись

                GameObject go = new GameObject($"Player_{id}");
                var rp = go.AddComponent<RemotePlayer>();
                rp.Init();
                Players.Add(id, rp);
                MelonLogger.Msg($"[Main] Создан новый удаленный игрок ID: {id}");
            }
            Players[id].UpdateSync(pos, rot, Crouch);

        }

        public void RemovePlayer(int id) { if (Players.TryGetValue(id, out var rp)) { Object.Destroy(rp.gameObject); Players.Remove(id); } }
        public void StartGameTogether() { _lobby.SendStartGame(); Il2Cpp.GameManager.LoadSceneWithLoadingScreen(_lobby.regions[_lobby.selectedRegionIdx]); }
        public override void OnGUI() => _lobby.OnGUI();
    }
}