using EarthVR.Navigation;
using NUnit.Framework;

namespace EarthVR.Tests
{
    public sealed class NavigationLogicTests
    {
        [Test]
        public void StateTransitionsAreInputIndependent()
        {
            var state = new NavigationState();
            Assert.That(state.Mode, Is.EqualTo(MovementMode.Flight));
            state.Toggle();
            Assert.That(state.Mode, Is.EqualTo(MovementMode.Grounded));
            state.SetMode(MovementMode.Flight);
            Assert.That(state.Mode, Is.EqualTo(MovementMode.Flight));
        }

        [Test]
        public void FlightSpeedGrowsWithAltitudeAndScaleButClamps()
        {
            var walking = FlightSpeedModel.CalculateGeographicSpeed(0f, 1f, 1.8f, 18f, 0.65f, 100000f);
            var city = FlightSpeedModel.CalculateGeographicSpeed(1000f, 10f, 1.8f, 18f, 0.65f, 100000f);
            var globe = FlightSpeedModel.CalculateGeographicSpeed(10000000f, 1000f, 1.8f, 18f, 0.65f, 1000f);
            Assert.That(walking, Is.EqualTo(1.8f).Within(0.001f));
            Assert.That(city, Is.GreaterThan(walking));
            Assert.That(globe, Is.EqualTo(1000f));
        }
    }
}
