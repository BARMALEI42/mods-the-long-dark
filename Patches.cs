using HarmonyLib;
using Il2Cpp;
using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using UnityEngine;
using Il2CppTLD.IntBackedUnit; // Обязательно для работы с m_Units

namespace TLD_Multiplayer
{
    [HarmonyPatch(typeof(Il2Cpp.BreakDown), "DoBreakDown", new System.Type[] { typeof(bool) })]
    public class BreakPatch
    {
        public static void Postfix(Il2Cpp.BreakDown __instance, bool spawnYieldObjects)
        {
            if (MainMod.Instance == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ObjectDestroy);
            writer.Put(__instance.transform.position.x);
            writer.Put(__instance.transform.position.y);
            writer.Put(__instance.transform.position.z);

            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            MelonLogger.Msg($"[Отправка] Объект {__instance.name} сломан.");
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.PlayerManager), "ProcessPickupItemInteraction", new System.Type[] { typeof(Il2Cpp.GearItem), typeof(bool), typeof(bool), typeof(bool) })]
    public class PickupPatch
    {
        public static void Postfix(Il2Cpp.GearItem item)
        {
            if (item == null || MainMod.Instance == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ItemPickup);
            writer.Put(item.transform.position.x);
            writer.Put(item.transform.position.y);
            writer.Put(item.transform.position.z);

            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.GearItem), "Drop", new System.Type[] { typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    public class DropPatch
    {
        public static void Postfix(Il2Cpp.GearItem __instance, int numUnits, bool playSound, bool stickToFeet, bool force)
        {
            if (MainMod.Instance == null || __instance == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)PacketType.ItemDrop);

            string cleanName = __instance.name.Replace("(Clone)", "").Trim();
            writer.Put(cleanName);
            writer.Put(__instance.transform.position.x);
            writer.Put(__instance.transform.position.y);
            writer.Put(__instance.transform.position.z);

            writer.Put(__instance.GetNormalizedCondition());
            writer.Put(numUnits);

            float extraData = 0f;
            if (__instance.m_FoodItem != null)
            {
                extraData = __instance.m_FoodItem.m_CaloriesRemaining;
            }
            else if (__instance.m_KeroseneLampItem != null)
            {
                // Чтение значения из структуры IntBackedUnit (через .m_Units)
                extraData = __instance.m_KeroseneLampItem.m_CurrentFuelLiters.m_Units;
            }
            else if (__instance.m_LiquidItem != null)
            {
                // Чтение воды (поле m_Liquid найдено через UnityExplorer)
                extraData = __instance.m_LiquidItem.m_Liquid.m_Units;
            }

            writer.Put(extraData);
            MainMod.Instance._netHandler.Manager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            MelonLogger.Msg($"[DEBUG] Выброшен {cleanName}, состояние: {__instance.GetNormalizedCondition() * 100}%");
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

    [HarmonyPatch(typeof(Il2Cpp.Panel_Rest), "DoRest")]
    public class SleepPatch { public static void Postfix() => MainMod.Instance.SendSleepVote(true); }

    [HarmonyPatch(typeof(Il2Cpp.PassTime), "Begin", new System.Type[] { typeof(float), typeof(Il2Cpp.Bed) })]
    public class WaitPatch { public static void Postfix() => MainMod.Instance.SendSleepVote(true); }
}