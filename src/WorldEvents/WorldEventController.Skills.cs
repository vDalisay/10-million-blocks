using System;
using Godot;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.WorldEvents;

public partial class WorldEventController
{
    private const double AutomaticCloudChargeIntervalSeconds = 3.0;

    private SkillTreeService? _eventSkills;
    private Timer? _cloudChargerTimer;

    /// <summary>
    /// Connects player-bound event upgrades without making WorldEventController own progression state.
    /// The charger only contributes presentation/gameplay input to the existing authoritative cloud
    /// mechanic; ChargeCloud still decides when a strike fires and MiningService still owns removals.
    /// </summary>
    public void AttachSkills(SkillTreeService skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (ReferenceEquals(_eventSkills, skills))
        {
            RefreshCloudCharger();
            return;
        }

        if (_eventSkills is not null) _eventSkills.Changed -= RefreshCloudCharger;
        _eventSkills = skills;
        _eventSkills.Changed += RefreshCloudCharger;
        RefreshCloudCharger();
    }

    private void RefreshCloudCharger()
    {
        bool enabled = _cloudEnabled
            && _eventSkills is not null
            && _eventSkills.Derived.AutoCloudChargerUnlocked;

        if (!enabled)
        {
            _cloudChargerTimer?.Stop();
            RefreshStatus();
            return;
        }

        if (_cloudChargerTimer is null)
        {
            _cloudChargerTimer = new Timer
            {
                Name = "AutomaticCloudCharger",
                WaitTime = AutomaticCloudChargeIntervalSeconds,
                OneShot = false,
                Autostart = false,
            };
            _cloudChargerTimer.Timeout += OnAutomaticCloudCharge;
            AddChild(_cloudChargerTimer);
        }

        if (_cloudChargerTimer.IsStopped()) _cloudChargerTimer.Start();
        RefreshStatus();
    }

    private void OnAutomaticCloudCharge()
    {
        if (_cloud is null
            || _eventSkills is null
            || !_eventSkills.Derived.AutoCloudChargerUnlocked
            || !_cloudEnabled)
        {
            return;
        }

        ChargeCloud();
        RequestPersistence();
    }

    private string CloudChargerStatus()
        => _eventSkills?.Derived.AutoCloudChargerUnlocked == true
            ? "   |   Cloud Charger: AUTO"
            : string.Empty;
}
