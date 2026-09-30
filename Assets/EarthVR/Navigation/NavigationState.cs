using System;

namespace EarthVR.Navigation
{
    public enum MovementMode
    {
        Flight,
        Grounded,
        Car
    }

    public sealed class NavigationState
    {
        public MovementMode Mode { get; private set; } = MovementMode.Flight;
        public event Action<MovementMode> ModeChanged;

        public void Toggle() => SetMode(Mode == MovementMode.Flight ? MovementMode.Grounded : MovementMode.Flight);

        public void SetMode(MovementMode mode)
        {
            if (Mode == mode)
                return;
            Mode = mode;
            ModeChanged?.Invoke(Mode);
        }
    }
}
