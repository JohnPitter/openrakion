namespace RakionServer.World.Domain
{
    public sealed class InventoryUiState
    {
        private readonly object _sync = new();
        private bool _open;

        public byte Open(bool mutationInProgress)
        {
            lock (_sync)
            {
                if (mutationInProgress) return 2;
                if (_open) return 1;
                _open = true;
                return 0;
            }
        }

        public byte Close(bool mutationInProgress)
        {
            lock (_sync)
            {
                if (mutationInProgress) return 2;
                if (!_open) return 1;
                _open = false;
                return 0;
            }
        }

        public byte MutationStatus(bool mutationInProgress)
        {
            lock (_sync)
            {
                if (!_open) return 1;
                return mutationInProgress ? (byte)2 : (byte)0;
            }
        }
    }
}
