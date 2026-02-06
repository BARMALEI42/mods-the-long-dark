using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using Il2Cpp;
using MelonLoader;
using System.Net;
using Il2CppTLD.IntBackedUnit; // Важно для типов структур

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
            Manager.UpdateTime = 15;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                PacketType type = (PacketType)reader.GetByte();
                switch (type)
                {
                    case PacketType.PlayerSync: HandlePlayerSync(peer, reader); break;
                    case PacketType.ObjectDestroy: HandleObjectDestroy(peer, reader); break;
                    case PacketType.ItemPickup: HandleItemPickup(peer, reader); break;
                    case PacketType.ItemDrop: HandleItemDrop(peer, reader); break;
                    case PacketType.ContainerUpdate: HandleContainerUpdate(peer, reader); break;
                    case PacketType.FireSpawn: HandleFireSpawn(peer, reader); break;
                    case PacketType.WorldSync: HandleWorldSync(peer, reader); break;
                    case PacketType.SleepVote: HandleSleepVote(peer, reader); break;
                    case PacketType.LobbyUpdate:
                        if (!_mod.isServer)
                        {
                            _mod._lobby.selectedRegionIdx = reader.GetInt();
                            _mod._lobby.selectedDiffIdx = reader.GetInt();
                        }
                        break;
                    case PacketType.StartGame:
                        if (!_mod.isServer) Il2Cpp.GameManager.LoadSceneWithLoadingScreen(_mod._lobby.regions[reader.GetInt()]);
                        break;
                }
            }
            catch (System.Exception ex) { MelonLogger.Error($"[Network] Ошибка: {ex.Message}"); }
        }

        private void HandlePlayerSync(NetPeer peer, NetPacketReader reader)
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
                relay.Put(rot); relay.Put(crouch);
                Manager.SendToAll(relay, DeliveryMethod.Unreliable, peer);
            }

            int myOwnId = _mod.isServer ? -1 : (Manager.FirstPeer != null ? Manager.FirstPeer.Id : -2);
            if (playerId != myOwnId) _mod.SyncRemotePlayer(playerId, pos, rot, crouch);
        }

        private void HandleItemDrop(NetPeer peer, NetPacketReader reader)
        {
            string name = reader.GetString();
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            float condition = reader.GetFloat();
            int units = reader.GetInt();
            float extraData = reader.GetFloat();

            if (_mod.isServer)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.ItemDrop);
                writer.Put(name);
                writer.Put(pos.x); writer.Put(pos.y); writer.Put(pos.z);
                writer.Put(condition);
                writer.Put(units);
                writer.Put(extraData);
                Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
            }

            var prefab = GearItem.LoadGearItemPrefab(name);
            if (prefab == null) return;

            var spawned = GameManager.GetPlayerManagerComponent().InstantiateItemAtLocation(prefab, units, pos, true);
            if (spawned == null) return;

            // 1. Установка прочности
            if (spawned.m_GearItemData != null)
            {
                spawned.m_CurrentHP = condition * spawned.m_GearItemData.m_MaxHP;
            }

            // 2. Установка специфических данных (Исправлено приведение типов)
            if (spawned.m_FoodItem != null)
            {
                spawned.m_FoodItem.m_CaloriesRemaining = extraData;
            }
            else if (spawned.m_KeroseneLampItem != null)
            {
                var fuelStruct = spawned.m_KeroseneLampItem.m_CurrentFuelLiters;
                fuelStruct.m_Units = (long)extraData; // ДОБАВЛЕНО ЯВНОЕ ПРИВЕДЕНИЕ
                spawned.m_KeroseneLampItem.m_CurrentFuelLiters = fuelStruct;
            }
            else if (spawned.m_LiquidItem != null)
            {
                var liquidStruct = spawned.m_LiquidItem.m_Liquid;
                liquidStruct.m_Units = (long)extraData; // ДОБАВЛЕНО ЯВНОЕ ПРИВЕДЕНИЕ
                spawned.m_LiquidItem.m_Liquid = liquidStruct;
            }

            MelonLogger.Msg($"[Network] Синхронизирован предмет: {name} ({condition * 100}%)");
        }

        private void HandleObjectDestroy(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.ObjectDestroy, pos, peer);

            var hitColliders = Physics.OverlapSphere(pos, 1.5f);
            foreach (var col in hitColliders)
            {
                var bd = col.gameObject.GetComponentInParent<BreakDown>();
                if (bd != null) { UnityEngine.Object.Destroy(bd.gameObject); break; }
            }
        }

        private void HandleItemPickup(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.ItemPickup, pos, peer);

            var hitColliders = Physics.OverlapSphere(pos, 2.5f);
            foreach (var col in hitColliders)
            {
                var gi = col.gameObject.GetComponentInParent<GearItem>();
                if (gi != null && !gi.m_InPlayerInventory) { UnityEngine.Object.Destroy(gi.gameObject); break; }
            }
        }

        private void HandleContainerUpdate(NetPeer peer, NetPacketReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (_mod.isServer) RelayPacket(PacketType.ContainerUpdate, pos, peer);

            foreach (var c in Object.FindObjectsOfType<Container>())
            {
                if (Vector3.Distance(c.transform.position, pos) < 1.0f) { c.MarkAsInspected(); break; }
            }
        }

        private void HandleWorldSync(NetPeer peer, NetPacketReader reader)
        {
            if (_mod.isServer) return;
            float time = reader.GetFloat();
            int weather = reader.GetInt();
            if (TimeOfDay.Instance != null) TimeOfDay.Instance.SetNormalizedTime(time);
            var wc = GameManager.GetWeatherComponent();
            if (wc != null) wc.m_CurrentWeatherStage = (WeatherStage)weather;
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
            var fm = GameManager.GetFireManagerComponent();
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

        public void OnPeerConnected(NetPeer peer) { if (_mod.isServer) _mod._lobby.SendLobbyUpdate(); }
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => _mod.RemovePlayer(peer.Id);
        public void OnConnectionRequest(ConnectionRequest req) => req.AcceptIfKey("TLD_MP");
        public void OnNetworkError(IPEndPoint ep, System.Net.Sockets.SocketError err) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int lat) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
    }
}