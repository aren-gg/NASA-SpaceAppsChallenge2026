using System.Reflection;
using MarsTrainer.Look;
using MarsTrainer.Gameplay.Weather;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using MarsWeather;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MarsTrainer.Tests
{
    public sealed class LookPresentationTests
    {
        [Test]
        public void SuitShellUsesSharedPaletteMaterialForRadiationState()
        {
            var owner = new GameObject("P11 suit state");
            try
            {
                var profile = Resources.Load<MarsLookProfile>("MarsLook");
                Assert.That(profile, Is.Not.Null);
                var sim = new Simulation();
                var radiation = new RadiationSystem(sim.Clock, _ => new RadiationConditions(0, 0), sim.Modifiers, sim.Events);
                sim.RegisterSystem(radiation);
                var weather = owner.AddComponent<WeatherController>(); weather.look = profile;
                var presenter = owner.AddComponent<WeatherPresenter>(); presenter.visuals = weather;
                var shell = new GameObject("Suit shell", typeof(MeshRenderer)); shell.transform.SetParent(owner.transform);
                presenter.suitShell = shell.GetComponent<Renderer>();
                presenter.Initialize(sim);
                typeof(WeatherPresenter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
                Assert.That(presenter.suitShell.sharedMaterial, Is.SameAs(profile.safeShell));
                sim.State.RadiationDoseMSv = GameConstants.H_limit;
                radiation.Step(sim.State, .001);
                typeof(WeatherPresenter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
                Assert.That(presenter.ShieldState, Is.EqualTo(SuitShieldState.Critical));
                Assert.That(presenter.suitShell.sharedMaterial, Is.SameAs(profile.criticalShell));
                presenter.Initialize(new Simulation());
                typeof(WeatherPresenter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
                Assert.That(presenter.suitShell.sharedMaterial, Is.SameAs(profile.safeShell));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void ColdRequiresNightStormRampsAndAtmosphereRestoresOnDisable()
        {
            var owner = new GameObject("P11 visual test");
            var look = ScriptableObject.CreateInstance<MarsLookProfile>();
            var weather = owner.AddComponent<WeatherController>();
            look.edgeMaterial = Resources.Load<MarsLookProfile>("MarsLook").edgeMaterial;
            weather.look = look; weather.transitionSeconds = 0;
            Color fog = RenderSettings.fogColor;
            float density = RenderSettings.fogDensity;
            var mode = RenderSettings.fogMode;
            var ambient = RenderSettings.ambientLight;
            try
            {
                Invoke(weather, "OnEnable");
                Assert.That(owner.GetComponentInChildren<RawImage>().mainTexture, Is.SameAs(Texture2D.whiteTexture));
                weather.SetPresentation(0, 0, 0, 1, 12, false);
                weather.AdvanceVisuals(1);
                Assert.That(weather.NightAmount, Is.Zero);
                Assert.That(weather.ColdAmount, Is.Zero);
                weather.SetPresentation(0, 0, 0, 1, 0, false);
                weather.AdvanceVisuals(1);
                Assert.That(weather.NightAmount, Is.EqualTo(1));
                Assert.That(weather.ColdAmount, Is.EqualTo(1));
                weather.transitionSeconds = 4;
                weather.SetPresentation(1, 1, 0, 0, 12, true);
                weather.AdvanceVisuals(1);
                Assert.That(weather.DustAmount, Is.EqualTo(.25f));
                Assert.That(weather.SolarAmount, Is.EqualTo(.25f));
                Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.ExponentialSquared));
                Assert.That(RenderSettings.fogDensity, Is.GreaterThan(look.clearFog));
                Invoke(weather, "OnDisable");
                Assert.That(RenderSettings.fogColor, Is.EqualTo(fog));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(density));
                Assert.That(RenderSettings.fogMode, Is.EqualTo(mode));
                Assert.That(RenderSettings.ambientLight, Is.EqualTo(ambient));
            }
            finally
            {
                Invoke(weather, "OnDisable");
                Object.DestroyImmediate(owner); Object.DestroyImmediate(look);
            }
        }
        private static void Invoke(WeatherController weather, string name)
        {
            typeof(WeatherController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(weather, null);
        }
    }
}
