using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using MarsTrainer.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MarsTrainer.Tests
{
    public sealed class DataRepositoryTests
    {
        [UnityTest]
        public IEnumerator BundledFilesLoad668ValidSolsAndCrops()
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            yield return repository.Load();
            Assert.That(warnings, Is.Empty);
            Assert.That(repository.Schedule, Has.Count.EqualTo(668));
            Assert.That(repository.Crops, Has.Count.GreaterThanOrEqualTo(3));
            for (int sol = 0; sol < repository.Schedule.Count; sol++)
            {
                var row = repository.Schedule[sol];
                Assert.That(row.sol, Is.EqualTo(sol));
                Assert.That(row.ls, Is.InRange(0f, 359.9999f));
                Assert.That(row.minTemp, Is.InRange(-150f, 50f));
                Assert.That(row.maxTemp, Is.InRange(row.minTemp, 50f));
                Assert.That(row.dustOpacity, Is.InRange(0f, 10f));
                Assert.That(row.gcrDose_uGy, Is.InRange(0f, 2000f));
                Assert.That(row.flareLevel, Is.InRange(0, 3));
                Assert.That(row.source, Is.Not.Null.And.Not.Empty);
            }
            foreach (var crop in repository.Crops)
            {
                Assert.That(crop.daysToHarvest, Is.GreaterThan(0));
                Assert.That(crop.kcalPerHarvest, Is.GreaterThan(0));
                Assert.That(crop.lightHours, Is.InRange(0.01f, 24f));
            }
        }

        [UnityTest]
        public IEnumerator MissingFilesUseDefaultsAndWarn()
        {
            var repository = new DataRepository();
            string missing = Path.Combine(Application.temporaryCachePath, Guid.NewGuid().ToString());
            LogAssert.Expect(LogType.Warning, "Schedule unavailable or invalid; using defaults.");
            LogAssert.Expect(LogType.Warning, "Crops unavailable or invalid; using defaults.");
            yield return repository.Load(missing);
            Assert.That(repository.Schedule, Has.Count.EqualTo(668));
            Assert.That(repository.Schedule[0].source, Is.EqualTo("synthetic"));
            Assert.That(repository.Crops, Has.Count.EqualTo(3));
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("[{}]")]
        [TestCase("garbage")]
        public void MalformedScheduleWarnsAndResets(string json)
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            repository.LoadScheduleJson(json);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(repository.Schedule, Has.Count.EqualTo(668));
        }

        [TestCase("\"gcrDose_uGy\": 210", "\"gcrDose_uGy\": -1")]
        [TestCase("\"minTemp\": -85", "\"minTemp\": 90")]
        [TestCase("\"ls\": 0", "\"ls\": 360")]
        [TestCase("\"flareLevel\": 0", "\"flareLevel\": 4")]
        [TestCase("\"dustOpacity\": 0.35", "\"dustOpacity\": \"NaN\"")]
        public void InvalidRangesRejectEntireSchedule(string before, string after)
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            // Full valid fixture, only the first row is corrupted.
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "mars_year_schedule.json"));
            string first = "{\"sol\":0,\"ls\":0,\"minTemp\":-85,\"maxTemp\":-20,\"dustOpacity\":0.35,\"gcrDose_uGy\":210,\"flareLevel\":0,\"auroraAlert\":false,\"source\":\"RAD\"}";
            first = first.Replace("\":", "\": ");
            int end = json.IndexOf('}');
            json = "[" + first.Replace(before, after) + json.Substring(end + 1);
            repository.LoadScheduleJson(json);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(repository.Schedule[0].source, Is.EqualTo("synthetic"));
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("[{}]")]
        [TestCase("[{\"id\":\"x\",\"name\":\"x\",\"daysToHarvest\":1,\"kcalPerHarvest\":1,\"parSat\":1,\"waterLPerDay\":1,\"co2Ppm\":1,\"lightHours\":25,\"needsVerification\":true}]")]
        public void InvalidCropsUseDefaults(string json)
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            repository.LoadCropsJson(json);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(repository.Crops, Has.Count.EqualTo(3));
        }

        [Test]
        public void MissingRequiredFieldRejectsFullYear()
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "mars_year_schedule.json"));
            json = json.Replace("\"auroraAlert\": true,", "").Replace("\"auroraAlert\": false,", "");
            repository.LoadScheduleJson(json);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(repository.Schedule, Has.Count.EqualTo(668));
        }

        [Test]
        public void DuplicateCropIdsResetOnlyCrops()
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            var schedule = repository.Schedule;
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "crop_parameters.json"));
            repository.LoadCropsJson(json.Replace("\"mizuna\"", "\"romaine\""));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(repository.Schedule, Is.SameAs(schedule));
            Assert.That(repository.Crops[1].id, Is.EqualTo("mizuna"));
        }

        [UnityTest]
        public IEnumerator RemotePathsAreRejectedWithoutNetwork()
        {
            var warnings = new List<string>();
            var repository = new DataRepository(warnings.Add);
            yield return repository.Load("https://example.invalid");
            Assert.That(warnings, Has.Count.EqualTo(2));
            Assert.That(repository.Schedule, Has.Count.EqualTo(668));
        }
    }
}
