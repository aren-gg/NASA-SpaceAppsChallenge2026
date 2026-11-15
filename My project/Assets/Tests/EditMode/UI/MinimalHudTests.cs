using MarsTrainer.UI;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class MinimalHudTests
    {
        [Test]
        public void DefaultHudKeepsDetailedActionsAndMetersOffTheWorldView()
        {
            var root = HudView.Create();
            try
            {
                Assert.That(root.GetComponentInChildren<MissionControls>(), Is.Null, "Detailed actions belong in the closed field kit.");
                Assert.That(root.GetComponentInChildren<SunZapMeterView>(), Is.Null, "The large radiation readout must not cover exploration.");
                Assert.That(root.GetComponentsInChildren<StatBar>().Length, Is.EqualTo(5));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
