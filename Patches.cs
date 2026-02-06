using HarmonyLib;
using Il2Cpp;
using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using UnityEngine;

namespace TLD_Multiplayer
{
    // 1. Ломание мебели/объектов
    [HarmonyPatch(typeof(Il2Cpp.BreakDown), "DoBreakDown", new System.Type[] { typeof(bool) })]
    public class BreakPatch
    {
        public static void Postfix(Il2Cpp.BreakDown __instance, bool spawnYieldObjects)
        {
            // Проверяем, что это не мы сами удалили объект по сети
            if (MainMod.Instance == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ObjectDestroy);
            writer.Put(__instance.transform.position.x);
            writer.Put(__instance.transform.position.y);
            writer.Put(__instance.transform.position.z);

            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            MelonLogger.Msg($"[Отправка] Объект {__instance.name} сломан, координаты отправлены.");
        }
    }

    // 2. ПОДНЯТИЕ ПРЕДМЕТОВ (Исправлено для версии 2.5.1)
    [HarmonyPatch(typeof(Il2Cpp.PlayerManager), "ProcessPickupItemInteraction", new System.Type[] { typeof(Il2Cpp.GearItem), typeof(bool), typeof(bool), typeof(bool) })]
    public class PickupPatch
    {
        // Мы указываем только те аргументы, которые нам нужны (item), 
        // Harmony сам поймет, что остальные нужно пропустить.
        public static void Postfix(Il2Cpp.GearItem item)
        {
            if (item == null || MainMod.Instance == null) return;

            MelonLogger.Msg($"[DEBUG] Поднят предмет: {item.name}");

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ItemPickup);
            writer.Put(item.transform.position.x);
            writer.Put(item.transform.position.y);
            writer.Put(item.transform.position.z);

            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    // 3. Исправленный хук на выбрасывание предметов
    [HarmonyPatch(typeof(Il2Cpp.GearItem), "Drop", new System.Type[] { typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    public class DropPatch
    {
        public static void Postfix(Il2Cpp.GearItem __instance, int numUnits, bool playSound, bool stickToFeet, bool force)
        {
            if (MainMod.Instance == null || __instance == null) return;

            // Если предмет выброшен, он больше не в инвентаре, и мы шлем его координаты
            MelonLogger.Msg($"[DEBUG] Предмет {__instance.name} выброшен. Отправка в сеть...");

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ItemDrop);

            // Очищаем имя, чтобы LoadGearItemPrefab у другого игрока сработал
            string cleanName = __instance.name.Replace("(Clone)", "").Trim();
            writer.Put(cleanName);

            writer.Put(__instance.transform.position.x);
            writer.Put(__instance.transform.position.y);
            writer.Put(__instance.transform.position.z);

            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Container))]
    public class ContainerItemPatch
    {
        [HarmonyPatch("AddGear", new System.Type[] { typeof(Il2Cpp.GearItem) })]
        [HarmonyPostfix]
        public static void PostAdd(Il2Cpp.Container __instance) => Sync(__instance);

        [HarmonyPatch("RemoveGear", new System.Type[] { typeof(Il2Cpp.GearItem), typeof(bool) })]
        [HarmonyPostfix]
        public static void PostRem(Il2Cpp.Container __instance) => Sync(__instance);

        private static void Sync(Il2Cpp.Container c)
        {
            if (MainMod.Instance == null || c == null) return;
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ContainerUpdate);
            writer.Put(c.transform.position.x); writer.Put(c.transform.position.y); writer.Put(c.transform.position.z);
            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    // 5. Костры (Используем InstantiateCampFire из твоего дампа)
    [HarmonyPatch(typeof(Il2Cpp.FireManager), "InstantiateCampFire")]
    public class FireSpawnPatch
    {
        public static void Postfix(Il2Cpp.Fire __result)
        {
            if (MainMod.Instance == null || __result == null) return;
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.FireSpawn);
            writer.Put(__result.transform.position.x);
            writer.Put(__result.transform.position.y);
            writer.Put(__result.transform.position.z);
            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    // 6. Сон и Ожидание (PassTime вместо Panel_Wait)
    [HarmonyPatch(typeof(Il2Cpp.Panel_Rest), "DoRest")]
    public class SleepPatch { public static void Postfix() => MainMod.Instance.SendSleepVote(true); }

    [HarmonyPatch(typeof(Il2Cpp.PassTime), "Begin", new System.Type[] { typeof(float), typeof(Il2Cpp.Bed) })]
    public class WaitPatch { public static void Postfix() => MainMod.Instance.SendSleepVote(true); }
}