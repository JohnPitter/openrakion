namespace RakionServer.World.Domain
{
    public static class CharacterLifecycleRules
    {
        public static bool CanCreate(
            int gameInfoId, int activeCharacterId, byte characterClass, byte slot) =>
            gameInfoId > 0 && activeCharacterId == 0 && characterClass < 5 && slot < 6;

        public static bool CanSelect(int gameInfoId, int activeCharacterId, int characterId) =>
            gameInfoId > 0 && activeCharacterId == 0 && characterId != 0;
    }
}
