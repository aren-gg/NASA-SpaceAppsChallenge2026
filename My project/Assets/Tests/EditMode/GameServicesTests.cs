using MarsTrainer.Core;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class GameServicesTests
    {
        [Test]
        public void BootstrapServicesAreUnassigned()
        {
            Assert.That(GameServices.SimClock, Is.Null);
            Assert.That(GameServices.GameState, Is.Null);
            Assert.That(GameServices.DataRepository, Is.Null);
        }
    }
}
