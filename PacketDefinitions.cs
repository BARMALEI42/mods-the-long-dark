namespace TLD_Multiplayer
{
    public enum PacketType : byte
    {
        LobbyUpdate = 1, StartGame = 2, PlayerSync = 3, WorldSync = 4,
        ObjectDestroy = 5,   // Мебель, шторы, ветки
        ContainerUpdate = 6, // Синхронизация содержимого ящиков
        ItemPickup = 7,      // Поднятие вещи (мир/ящик)
        ItemDrop = 8,        // Выбрасывание вещи на пол
        FireSpawn = 9,       // Появление костра
        SleepVote = 10,      // Голосование (Сон + Ожидание)
        Disconnect = 11
    }
}