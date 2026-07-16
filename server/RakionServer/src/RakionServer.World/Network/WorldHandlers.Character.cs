namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_CharacterCreate(HandlerContext context) =>
            context.User.BeginCharacterCreate(context.Raw);

        private static void Op_CharacterDelete(HandlerContext context) =>
            context.User.BeginCharacterDelete(context.Raw);

        private static void Op_CharacterSelect(HandlerContext context) =>
            context.User.BeginCharacterSelect(context.Raw);

        private static void Op_CharacterChangeBuddyName(HandlerContext context) =>
            context.User.BeginBuddyNameChange(context.Raw);

        private static void Op_CharacterTutorialClear(HandlerContext context) =>
            context.User.BeginCharacterTutorialClear(context.Raw);

        private static void Op_CharacterStateClear(HandlerContext context) =>
            context.User.BeginCharacterStateClear(context.Raw);

        private static void Op_CharacterChangeCharName(HandlerContext context) =>
            context.User.BeginCharacterRename(context.Raw);
    }
}
