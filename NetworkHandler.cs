using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using Il2Cpp;
using MelonLoader;
using System.Net;

namespace TLD_Multiplayer
{
    public class NetworkHandler : INetEventListener
    {
        public NetManager Manager;
        private MainMod _mod;

        public NetworkHandler(MainMod mod)
        {
            _mod = mod;
            Manager = new NetManager(this);
            Manager.UpdateTime = 15; // Частота обновления сети
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                PacketType type = (PacketType)reader.GetByte();

                switch (type)
                {
                    case PacketType.PlayerSync:
                        HandlePlayerSync(peer, reader);
                        break;

                    case PacketType.ObjectDestroy:
                        HandleObjectDestroy(peer, reader);
                        break;

                    case PacketType.ItemPickup:
                        HandleItemPickup(peer, reader);
                        break;

                    case PacketType.ItemDrop:
                        HandleItemDrop(peer, reader);
                        break;

                    case PacketType.ContainerUpdate:
                        HandleContainerUpdate(peer, reader);
                        break;

                    case PacketType.FireSpawn:
                        HandleFireSpawn(peer, reader);
                        break;

                    case PacketType.WorldSync:
                        HandleWorldSync(peer, reader);
                        break;

                    case PacketType.SleepVote:
                        HandleSleepVote(peer, reader);
                        break;

                    case PacketType.LobbyUpdate:
                        if (!_mod.isServer)
                        {
                            _mod._lobby.selectedRegionIdx = reader.GetInt();
                            _mod._lobby.selectedDiffIdx = reader.GetInt();
                            _mod._lobby.statusText = "Лобби обновлено хостом";
                        }
                        break;

                    case PacketType.StartGame:
                        if (!_mod.isServer)
                        {
                            int regionIdx = reader.GetInt();
                            MelonLogger.Msg($"[Network] Получен сигнал старта. Регион: {regionIdx}");
                            Il2Cpp.GameManager.LoadSceneWithLoadingScreen(_mod._lobby.regions[regionIdx]);
                        }
                        break;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[Network] Ошибка обработки пакета: {ex.Message}");
            }
        }

        // --- ОБРАБОТЧИКИ ПАКЕТОВ ---

        private void HandlePlayerSync(NetPeer peer, NetPacketReader reader)
        {
            try
            {
                int playerId = (reader.AvailableBytes > 13) ? reader.GetInt() : peer.Id;

                Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                float rot = reader.GetFloat();
                bool crouch = reader.GetBool();

                if (_mod.isServer)
                {
                    NetDataWriter relay = new NetDataWriter();
                    relay.Put((byte)PacketType.PlayerSync);
                    relay.Put(playerId);
                    relay.Put(pos.x); relay.Put(pos.y); relay.Put(pos.z);
                    relay.Put(rot);
                    relay.Put(crouch);
                    Manager.SendToAll(relay, DeliveryMethod.Unreliable, peer);
                }

                int myOwnId = _mod.isServer ? -1 : (Manager.FirstPeer != null ? Manager.FirstPeer.Id : -2);

                if (playerId != myOwnId)
                {
                    _mod.SyncRemotePlayer(playerId, pos, rot, crouch);
                }
            }
            catch { }
        }

        // ИСПРАВЛЕНО: Радиус 1.5f для мебели (стандарт стабильности)
        private void HandleObjectDestroy(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.ObjectDestroy, pos, peer);

            var hitColliders = UnityEngine.Physics.OverlapSphere(pos, 1.5f);
            foreach (var col in hitColliders)
            {
                var bd = col.gameObject.GetComponentInParent<Il2Cpp.BreakDown>();
                if (bd != null)
                {
                    MelonLogger.Msg($"[Сеть] Удаляем мебель (Радиус 1.5): {bd.gameObject.name}");
                    UnityEngine.Object.Destroy(bd.gameObject);
                    break;
                }
            }
        }

        // ИСПРАВЛЕНО: Радиус 2.5f как в Sky-Coop (для компенсации веток/камней)
        private void HandleItemPickup(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.ItemPickup, pos, peer);

            var hitColliders = UnityEngine.Physics.OverlapSphere(pos, 2.5f);
            foreach (var col in hitColliders)
            {
                var gi = col.gameObject.GetComponentInParent<Il2Cpp.GearItem>();
                if (gi != null && !gi.m_InPlayerInventory)
                {
                    MelonLogger.Msg($"[Сеть] Удаляем предмет (Радиус 2.5): {gi.name}");
                    UnityEngine.Object.Destroy(gi.gameObject);
                    break;
                }
            }
        }

        private void HandleItemDrop(NetPeer peer, NetPacketReader reader)
        {
            string name = reader.GetString().Replace("(Clone)", "").Trim();
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());

            if (_mod.isServer)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.ItemDrop);
                writer.Put(name);
                writer.Put(pos.x); writer.Put(pos.y); writer.Put(pos.z);
                Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
            }

            var prefab = Il2Cpp.GearItem.LoadGearItemPrefab(name);
            if (prefab != null)
            {
                Il2Cpp.GameManager.GetPlayerManagerComponent().InstantiateItemAtLocation(prefab, 1, pos, true);
            }
        }

        private void HandleContainerUpdate(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.ContainerUpdate, pos, peer);

            foreach (var c in Object.FindObjectsOfType<Il2Cpp.Container>())
            {
                // Для ящиков 1.0f достаточно
                if (Vector3.Distance(c.transform.position, pos) < 1.0f)
                {
                    c.MarkAsInspected();
                    MelonLogger.Msg($"[Network] Ящик обыскан: {c.name}");
                    break;
                }
            }
        }

        private void HandleWorldSync(NetPeer peer, NetPacketReader reader)
        {
            if (_mod.isServer) return;

            float time = reader.GetFloat();
            int weather = reader.GetInt();

            if (Il2Cpp.TimeOfDay.Instance != null)
                Il2Cpp.TimeOfDay.Instance.SetNormalizedTime(time);

            var wc = Il2Cpp.GameManager.GetWeatherComponent();
            if (wc != null)
                wc.m_CurrentWeatherStage = (Il2Cpp.WeatherStage)weather;
        }

        private void HandleSleepVote(NetPeer peer, NetPacketReader reader)
        {
            bool isSleeping = reader.GetBool();
            if (isSleeping) _mod.SleepingPlayers++; else _mod.SleepingPlayers--;

            if (_mod.isServer)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.SleepVote);
                writer.Put(isSleeping);
                Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
            }
        }

        private void HandleFireSpawn(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.FireSpawn, pos, peer);

            var fm = Il2Cpp.GameManager.GetFireManagerComponent();
            if (fm != null)
            {
                var fire = fm.InstantiateCampFire();
                if (fire != null) fire.transform.position = pos;
            }
        }

        private void RelayPacket(PacketType type, Vector3 pos, NetPeer excludePeer)
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)type);
            writer.Put(pos.x); writer.Put(pos.y); writer.Put(pos.z);
            Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered, excludePeer);
        }

        public void OnPeerConnected(NetPeer peer)
        {
            MelonLogger.Msg($"[Network] Подключился игрок: {peer.Id} ({peer})");
            if (_mod.isServer) _mod._lobby.SendLobbyUpdate();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            MelonLogger.Msg($"[Network] Игрок {peer.Id} отключился. Причина: {info.Reason}");
            _mod.RemovePlayer(peer.Id);
        }

        public void OnConnectionRequest(ConnectionRequest req) => req.AcceptIfKey("TLD_MP");
        public void OnNetworkError(IPEndPoint ep, System.Net.Sockets.SocketError err) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int lat) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
    }
}