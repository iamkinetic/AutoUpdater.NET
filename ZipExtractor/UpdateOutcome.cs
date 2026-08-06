namespace ZipExtractor
{
    /// <summary>
    ///     Result of an update attempt. An abort is a designed outcome that leaves the installation directory as it was,
    ///     which is why it carries whether the application can be started again.
    /// </summary>
    public class UpdateOutcome
    {
        private UpdateOutcome()
        {
        }

        public bool Succeeded { get; private set; }

        public string AbortReason { get; private set; }

        public bool ShouldRelaunchApplication { get; private set; }

        public static UpdateOutcome Success()
        {
            return new UpdateOutcome {Succeeded = true};
        }

        public static UpdateOutcome Aborted(string reason, bool shouldRelaunchApplication)
        {
            return new UpdateOutcome
            {
                Succeeded = false,
                AbortReason = reason,
                ShouldRelaunchApplication = shouldRelaunchApplication
            };
        }
    }
}
