using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class FieldPotionRulesTests
    {
        [Fact]
        public void EquippedPotionWithCount_IsAcceptedInUnlockedCell()
        {
            var context = new FieldPotionUseContext(13, 12001, 0, 3, 12001, 2, false);

            Assert.True(FieldPotionRules.CanUse(context));
        }

        [Fact]
        public void InvalidSlotItemOrCount_IsRejected()
        {
            FieldPotionUseContext[] invalid =
            {
                new(12, 12001, 0, 3, 12001, 1, false),
                new(16, 12001, 0, 3, 12001, 1, false),
                new(13, 11999, 0, 3, 11999, 1, false),
                new(13, 12001, 0, 3, 12002, 1, false),
                new(13, 12001, 0, 3, 12001, 0, false)
            };

            Assert.All(invalid, context => Assert.False(FieldPotionRules.CanUse(context)));
        }

        [Fact]
        public void UsedCell_IsReusableOnlyInModeZero()
        {
            var solo = new FieldPotionUseContext(13, 12001, 0, 3, 12001, 2, true);
            var battle = solo with { FieldMode = 2 };

            Assert.True(FieldPotionRules.CanUse(solo));
            Assert.False(FieldPotionRules.CanUse(battle));
        }

        [Fact]
        public void ConcurrentReservations_DoNotResurrectCommittedUnits()
        {
            var state = new FieldPotionReservationState(2, 0);
            Assert.True(FieldPotionRules.TryReserve(state, out state));
            Assert.True(FieldPotionRules.TryReserve(state, out state));
            Assert.Equal(new FieldPotionReservationState(0, 2), state);

            state = FieldPotionRules.Commit(state, databaseRemaining: 1);

            Assert.Equal(new FieldPotionReservationState(0, 1), state);
            Assert.False(FieldPotionRules.TryReserve(state, out _));
            Assert.Equal(new FieldPotionReservationState(0, 0),
                FieldPotionRules.Commit(state, databaseRemaining: 0));
        }

        [Fact]
        public void CommitSubtractsReservationsStillWaitingForDatabase()
        {
            var state = new FieldPotionReservationState(3, 2);

            FieldPotionReservationState committed = FieldPotionRules.Commit(
                state, databaseRemaining: 4);

            Assert.Equal(new FieldPotionReservationState(3, 1), committed);
        }

        [Fact]
        public void FailedPersistenceReleasesPendingWithoutRestoringConsumedEffect()
        {
            var state = new FieldPotionReservationState(0, 1);

            Assert.Equal(new FieldPotionReservationState(0, 0),
                FieldPotionRules.Fail(state));
        }
    }
}
