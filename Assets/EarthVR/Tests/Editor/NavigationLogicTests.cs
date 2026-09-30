using EarthVR.Core;
using EarthVR.Navigation;
using EarthVR.Scaling;
using EarthVR.Terrain;
using EarthVR.UI;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

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
        public void MovementToggleExitsCarToFlight()
        {
            var state = new NavigationState();
            state.SetMode(MovementMode.Car);
            state.Toggle();
            Assert.That(state.Mode, Is.EqualTo(MovementMode.Flight));
        }

        [Test]
        public void CarAccelerationAndBrakingUseSeparateRates()
        {
            var accelerating = CarDrivingMath.MoveSpeedTowards(0f, 20f, 5f, 12f, 1f);
            var braking = CarDrivingMath.MoveSpeedTowards(20f, 0f, 5f, 12f, 1f);
            Assert.That(accelerating, Is.EqualTo(5f).Within(0.001f));
            Assert.That(braking, Is.EqualTo(8f).Within(0.001f));
        }

        [Test]
        public void CarSteeringReversesWhileBackingUp()
        {
            var forward = CarDrivingMath.CalculateSteeringDelta(1f, 10f, 20f, 60f, 1f);
            var reverse = CarDrivingMath.CalculateSteeringDelta(1f, -10f, 20f, 60f, 1f);
            Assert.That(forward, Is.GreaterThan(0f));
            Assert.That(reverse, Is.EqualTo(-forward).Within(0.001f));
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

        [Test]
        public void FlightSpeedUsesAltitudeProportionalCruiseAtPlanetaryHeight()
        {
            var speed = FlightSpeedModel.CalculateGeographicSpeed(
                100000f,
                1f,
                1.8f,
                18f,
                0.65f,
                1000000f,
                0.35f);

            Assert.That(speed, Is.EqualTo(35000f).Within(0.01f));
        }

        [Test]
        public void GeometricHorizonGrowsWithAltitude()
        {
            var low = HighAltitudePresentationController.ComputeGeometricHorizonDistance(1000f);
            var high = HighAltitudePresentationController.ComputeGeometricHorizonDistance(100000f);

            Assert.That(low, Is.InRange(112000f, 114000f));
            Assert.That(high, Is.GreaterThan(1100000f));
            Assert.That(high, Is.GreaterThan(low));
        }

        [Test]
        public void FlightFarClipTracksCurvedHorizonInsteadOfFixedPlanetaryRange()
        {
            var low = NavigationController.CalculateGeographicFarClipDistance(
                300f,
                100000f,
                1.35f);
            var high = NavigationController.CalculateGeographicFarClipDistance(
                100000f,
                100000f,
                1.35f);
            var horizon = HighAltitudePresentationController.ComputeGeometricHorizonDistance(100000f);

            Assert.That(low, Is.EqualTo(100000f).Within(1f));
            Assert.That(high, Is.EqualTo(horizon * 1.35f).Within(2f));
            Assert.That(high, Is.GreaterThan(horizon));
        }

        [Test]
        public void TileRateLimitRetriesUseBoundedExponentialBackoff()
        {
            Assert.That(CesiumEarthProvider.CalculateRateLimitBackoffSeconds(1), Is.EqualTo(60f));
            Assert.That(CesiumEarthProvider.CalculateRateLimitBackoffSeconds(2), Is.EqualTo(120f));
            Assert.That(CesiumEarthProvider.CalculateRateLimitBackoffSeconds(5), Is.EqualTo(900f));
            Assert.That(CesiumEarthProvider.CalculateRateLimitBackoffSeconds(20), Is.EqualTo(900f));
        }

        [Test]
        public void TileFailureLogsRedactApiCredentials()
        {
            var sanitized = CesiumEarthProvider.SanitizeFailureMessage(
                "https://example.test/root.json?key=secret-value&other=1 access_token=also-secret");

            Assert.That(sanitized, Does.Not.Contain("secret-value"));
            Assert.That(sanitized, Does.Not.Contain("also-secret"));
            Assert.That(sanitized, Does.Contain("key=<redacted>"));
        }

        [Test]
        public void LateGoogleRateLimitCanBeSeparatedFromFallbackFailure()
        {
            Assert.That(
                CesiumEarthProvider.IsGooglePhotorealisticFailure(
                    "Received status code 429 for https://tile.googleapis.com/v1/3dtiles/root.json?key=secret"),
                Is.True);
            Assert.That(
                CesiumEarthProvider.IsGooglePhotorealisticFailure(
                    "Received status code 429 for https://assets.cesium.com/1/layer.json"),
                Is.False);
        }

        [Test]
        public void PlanetaryOverviewRaySelectsEllipsoidWithoutTerrainCollider()
        {
            var origin = new double3(EquatorialRadiusForTest + 1000000d, 0d, 0d);
            var hitFound = GlobeOverviewController.TryIntersectEllipsoid(
                origin,
                new double3(-1d, 0d, 0d),
                out var hit);

            Assert.That(hitFound, Is.True);
            Assert.That(hit.x, Is.EqualTo(EquatorialRadiusForTest).Within(0.01d));
            Assert.That(hit.y, Is.EqualTo(0d).Within(0.01d));
            Assert.That(hit.z, Is.EqualTo(0d).Within(0.01d));
        }

        [Test]
        public void PlanetaryOverviewRayRejectsSpaceMiss()
        {
            var origin = new double3(EquatorialRadiusForTest + 1000000d, 0d, 0d);
            Assert.That(
                GlobeOverviewController.TryIntersectEllipsoid(
                    origin,
                    new double3(0d, 1d, 0d),
                    out _),
                Is.False);
        }

        [Test]
        public void PlanetaryOverviewGreatCircleKeepsCameraOnGlobeOrbit()
        {
            var halfway = GlobeOverviewController.GreatCircleDirection(
                Vector3.forward,
                Vector3.right,
                0.5f);

            Assert.That(halfway.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Vector3.Angle(Vector3.forward, halfway), Is.EqualTo(45f).Within(0.01f));
            Assert.That(Vector3.Angle(halfway, Vector3.right), Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void PlanetaryOverviewGreatCircleHandlesAntipodalDestination()
        {
            var halfway = GlobeOverviewController.GreatCircleDirection(
                Vector3.forward,
                Vector3.back,
                0.5f);

            Assert.That(halfway.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Mathf.Abs(Vector3.Dot(halfway, Vector3.forward)), Is.LessThan(0.0001f));
        }

        [Test]
        public void PlanetaryTrackballKeepsCenterFixedAndGrabbedPointUnderPointer()
        {
            var center = new Vector3(3f, -2f, 8f);
            var cameraPosition = center + Vector3.forward * 10f;
            var cameraRotation = Quaternion.LookRotation(center - cameraPosition, Vector3.up);
            var grabbedSurfaceDirection = Vector3.forward;
            var currentPointerSurfaceDirection = new Vector3(0.5f, 0.2f, 1f).normalized;
            var orbit = GlobeOverviewController.CalculateTrackballCameraOrbit(
                grabbedSurfaceDirection,
                currentPointerSurfaceDirection);

            var orbitedPosition = center + orbit * (cameraPosition - center);
            var orbitedRotation = orbit * cameraRotation;
            var grabbedInNewCamera = Quaternion.Inverse(orbitedRotation) * grabbedSurfaceDirection;
            var pointerInOldCamera = Quaternion.Inverse(cameraRotation) * currentPointerSurfaceDirection;

            Assert.That(
                Vector3.Distance(orbitedPosition, center),
                Is.EqualTo(Vector3.Distance(cameraPosition, center)).Within(0.0001f));
            Assert.That(Vector3.Angle(grabbedInNewCamera, pointerInOldCamera), Is.LessThan(0.001f));
        }

        private const double EquatorialRadiusForTest = 6378137d;

        [Test]
        public void OfflineSuggestionsCompletePartialCityNameWithoutNetwork()
        {
            var suggestions = OfflinePlaceCatalog.FindSuggestions(
                "Los a",
                Array.Empty<SavedPlace>(),
                Array.Empty<SavedPlace>(),
                3);

            Assert.That(suggestions.Count, Is.GreaterThan(0));
            Assert.That(suggestions[0].Place.Name, Is.EqualTo("Los Angeles"));
            Assert.That(suggestions[0].Category, Is.EqualTo("City"));
        }

        [Test]
        public void OfflineSuggestionsIncludeNationalParks()
        {
            var suggestions = OfflinePlaceCatalog.FindSuggestions(
                "Yose",
                Array.Empty<SavedPlace>(),
                Array.Empty<SavedPlace>(),
                3);

            Assert.That(suggestions.Count, Is.GreaterThan(0));
            Assert.That(suggestions[0].Place.Name, Is.EqualTo("Yosemite National Park"));
            Assert.That(suggestions[0].Category, Is.EqualTo("National park"));
        }

        [Test]
        public void RandomStartingPlaceNeverImmediatelyRepeats()
        {
            Assert.That(OfflinePlaceCatalog.StartingPlaces.Count, Is.GreaterThan(2));
            for (var previous = 0; previous < OfflinePlaceCatalog.StartingPlaces.Count; previous++)
            {
                var chosen = OfflinePlaceCatalog.NormalizeStartingPlaceIndex(previous, previous);
                Assert.That(chosen, Is.Not.EqualTo(previous));
                Assert.That(chosen, Is.InRange(0, OfflinePlaceCatalog.StartingPlaces.Count - 1));
            }
        }

        [Test]
        public void RandomStartingPlacesIncludeParksAndCities()
        {
            Assert.That(OfflinePlaceCatalog.StartingPlaces.Any(place => place.Name == "Zion National Park"), Is.True);
            Assert.That(OfflinePlaceCatalog.StartingPlaces.Any(place => place.Name == "Tokyo"), Is.True);
        }

        [Test]
        public void WorldGrabConstraintPinsPointerEndpointToSelectedPoint()
        {
            var anchor = new Vector3(50f, 12f, -30f);
            var rotation = Quaternion.Euler(8f, 37f, -3f);
            var controllerLocal = new Vector3(0.25f, 1.2f, -0.4f);
            var pointerLocal = new Vector3(0.2f, -0.35f, 0.9f).normalized;
            const float distance = 42f;

            var navigationPosition = WorldGrabConstraintMath.CalculateNavigationPosition(
                anchor,
                rotation,
                controllerLocal,
                pointerLocal,
                distance);
            var solvedEndpoint = navigationPosition +
                                 rotation * (controllerLocal + pointerLocal * distance);

            Assert.That(Vector3.Distance(solvedEndpoint, anchor), Is.LessThan(0.0001f));
        }

        [Test]
        public void GroundedWorldGrabPinsAnchorAndTrackingFloorTogether()
        {
            var anchor = new Vector3(3f, 0f, 4f);
            var rotation = Quaternion.identity;
            var controllerLocal = new Vector3(0f, 1f, 0f);
            var pointerLocal = new Vector3(0f, -1f, 1f).normalized;
            var trackingFloorLocal = Vector3.zero;

            var navigationPosition = WorldGrabConstraintMath.CalculateGroundedNavigationPosition(
                anchor,
                rotation,
                controllerLocal,
                pointerLocal,
                trackingFloorLocal,
                0f,
                5f,
                out var solvedDistance);
            var solvedEndpoint = navigationPosition +
                                 rotation * (controllerLocal + pointerLocal * solvedDistance);
            var solvedFloorY = (navigationPosition + rotation * trackingFloorLocal).y;

            Assert.That(Vector3.Distance(solvedEndpoint, anchor), Is.LessThan(0.0001f));
            Assert.That(solvedFloorY, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(solvedDistance, Is.GreaterThan(0.01f));
        }

        [Test]
        public void RebasePoseMathPlacesChildAtDesiredPose()
        {
            var rootPosition = new Vector3(14f, -2f, 8f);
            var rootRotation = Quaternion.Euler(0f, 35f, 0f);
            var childPosition = new Vector3(16f, 1f, 11f);
            var childRotation = Quaternion.Euler(12f, 80f, -4f);
            var desiredPosition = new Vector3(0f, 0f, 0f);
            var desiredRotation = Quaternion.Euler(-5f, -20f, 9f);

            NavigationPoseMath.CalculateRootPose(
                rootPosition,
                rootRotation,
                childPosition,
                childRotation,
                desiredPosition,
                desiredRotation,
                out var newRootPosition,
                out var newRootRotation,
                out var delta);

            var transformedChildPosition = newRootPosition + delta * (childPosition - rootPosition);
            var transformedChildRotation = delta * childRotation;
            Assert.That(Vector3.Distance(transformedChildPosition, desiredPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(transformedChildRotation, desiredRotation), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(newRootRotation, delta * rootRotation), Is.LessThan(0.001f));
        }

        [Test]
        public void UprightResetRemovesTiltAndPreservesProjectedHeading()
        {
            var tilted = Quaternion.Euler(68f, 43f, 79f);
            var expectedHeading = Vector3.ProjectOnPlane(
                tilted * Vector3.forward,
                Vector3.up).normalized;

            var upright = NavigationController.CalculateUprightRotation(tilted);
            var actualHeading = Vector3.ProjectOnPlane(
                upright * Vector3.forward,
                Vector3.up).normalized;

            Assert.That(Vector3.Angle(upright * Vector3.up, Vector3.up), Is.LessThan(0.001f));
            Assert.That(Vector3.Angle(actualHeading, expectedHeading), Is.LessThan(0.001f));
        }

        [TestCase(0f, 1f, 0f, 0f, 90f)]
        [TestCase(0f, 0f, 1f, 0f, 0f)]
        [TestCase(0f, -1f, 0f, 0f, -90f)]
        [TestCase(1f, 0f, 0f, -90f, 0f)]
        public void MiniatureGlobeMapsSurfaceNormalsToLongitudeLatitude(
            float x, float y, float z, float expectedLongitude, float expectedLatitude)
        {
            var result = MiniatureGlobePicker.NormalToLongitudeLatitude(new Vector3(x, y, z));
            Assert.That(result.x, Is.EqualTo(expectedLongitude).Within(0.001f));
            Assert.That(result.y, Is.EqualTo(expectedLatitude).Within(0.001f));
        }

        [TestCase(-112.9874, 37.2982)]
        [TestCase(139.6917, 35.6895)]
        [TestCase(0.0, -82.0)]
        public void MiniatureGlobeCurrentLocationMarkerRoundTrips(double longitude, double latitude)
        {
            var normal = MiniatureGlobePicker.LongitudeLatitudeToNormal(longitude, latitude);
            var result = MiniatureGlobePicker.NormalToLongitudeLatitude(normal);
            Assert.That(result.x, Is.EqualTo(longitude).Within(0.001));
            Assert.That(result.y, Is.EqualTo(latitude).Within(0.001));
        }

        [Test]
        public void MiniatureGlobeDragMovesGrabbedSurfaceWithPointer()
        {
            var rotation = MiniatureGlobePicker.CalculateDragRotation(
                Quaternion.identity,
                Vector3.forward,
                Vector3.right);

            Assert.That(
                Vector3.Angle(rotation * Vector3.forward, Vector3.right),
                Is.LessThan(0.001f));
        }

        [Test]
        public void MiniatureGlobeMeshFacesOutward()
        {
            var mesh = MiniatureGlobePicker.CreateLongitudeLatitudeSphere(1f);
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var tested = false;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var faceNormal = Vector3.Cross(b - a, c - a);
                if (faceNormal.sqrMagnitude < 0.000001f)
                    continue;
                Assert.That(Vector3.Dot(faceNormal, (a + b + c) / 3f), Is.GreaterThan(0f));
                tested = true;
                break;
            }
            Assert.That(tested, Is.True);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void PlaceMatchingUsesLocationAndName()
        {
            var first = new SavedPlace { name = "Copenhagen", longitude = 12.5683, latitude = 55.6761 };
            var nearby = new SavedPlace { name = "copenhagen", longitude = 12.56835, latitude = 55.67615 };
            var differentlyNamed = new SavedPlace { name = "Home", longitude = 12.56835, latitude = 55.67615 };
            Assert.That(PlaceLibraryRules.IsSameDestination(first, nearby), Is.True);
            Assert.That(PlaceLibraryRules.IsSameDestination(first, differentlyNamed), Is.False);
        }

        [Test]
        public void PlaceMatchingWrapsAcrossAntimeridian()
        {
            var west = new SavedPlace { name = "Dateline", longitude = 179.99996, latitude = 10d };
            var east = new SavedPlace { name = "Dateline", longitude = -179.99996, latitude = 10d };

            Assert.That(PlaceLibraryRules.IsSameDestination(west, east), Is.True);
            Assert.That(
                PlaceLibraryRules.IsSameDestination(west, "Dateline", east.longitude, east.latitude),
                Is.True);
        }

        [Test]
        public void PreparingSavedPlaceRepairsNonFiniteViewpointValues()
        {
            var prepared = PlaceLibraryRules.Prepare(new SavedPlace
            {
                name = "Damaged",
                longitude = 12d,
                latitude = 55d,
                heightMeters = double.NaN,
                userScale = float.PositiveInfinity,
                headingDegrees = float.NaN
            });

            Assert.That(prepared.heightMeters, Is.EqualTo(0d));
            Assert.That(prepared.userScale, Is.EqualTo(1f));
            Assert.That(prepared.headingDegrees, Is.EqualTo(0f));
        }

        [Test]
        public void LocalPlaceLibraryPersistsBookmarksAndDeduplicatesRecents()
        {
            var path = Path.Combine(Application.temporaryCachePath, $"earthvr-place-test-{Guid.NewGuid():N}.json");
            try
            {
                var place = new SavedPlace
                {
                    name = "Test place",
                    longitude = 12.5683,
                    latitude = 55.6761,
                    heightMeters = 2500d,
                    userScale = 10f
                };
                var library = new LocalPlaceLibrary(path);
                library.SaveBookmark(place);
                library.RecordRecent(place);
                library.RecordRecent(place);

                var reloaded = new LocalPlaceLibrary(path);
                Assert.That(reloaded.Bookmarks.Count, Is.EqualTo(1));
                Assert.That(reloaded.RecentPlaces.Count, Is.EqualTo(1));
                Assert.That(reloaded.Bookmarks[0].userScale, Is.EqualTo(10f));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (File.Exists(path + ".tmp"))
                    File.Delete(path + ".tmp");
            }
        }
    }
}
