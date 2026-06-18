namespace WowVmMonitor.Core.Monitoring;

public sealed class LogMonitorStateMachine
{
    private readonly MonitorThresholds _thresholds;
    private readonly int _requiredAlertObservations;
    private readonly int _requiredRecoveryObservations;
    private int _consecutiveAlerts;
    private int _consecutiveNormals;
    private bool _isAlerting;

    public bool IsAlerting => _isAlerting;

    public LogMonitorStateMachine(
        MonitorThresholds thresholds,
        int requiredAlertObservations,
        int requiredRecoveryObservations)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (requiredAlertObservations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredAlertObservations));
        }

        if (requiredRecoveryObservations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredRecoveryObservations));
        }

        _thresholds = thresholds;
        _requiredAlertObservations = requiredAlertObservations;
        _requiredRecoveryObservations = requiredRecoveryObservations;
    }

    public MonitorEvaluation Evaluate(DateTimeOffset currentTime, DateTimeOffset latestLogTime)
    {
        var age = currentTime - latestLogTime;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var status = Classify(age);
        var transition = MonitorTransition.None;

        switch (status)
        {
            case LogActivityStatus.Alert:
                _consecutiveAlerts++;
                _consecutiveNormals = 0;
                if (!_isAlerting && _consecutiveAlerts >= _requiredAlertObservations)
                {
                    _isAlerting = true;
                    transition = MonitorTransition.Alert;
                }
                break;

            case LogActivityStatus.Normal:
                _consecutiveAlerts = 0;
                if (_isAlerting)
                {
                    _consecutiveNormals++;
                    if (_consecutiveNormals >= _requiredRecoveryObservations)
                    {
                        _isAlerting = false;
                        _consecutiveNormals = 0;
                        transition = MonitorTransition.Recovery;
                    }
                }
                else
                {
                    _consecutiveNormals = 0;
                }
                break;

            case LogActivityStatus.Warning:
                _consecutiveAlerts = 0;
                _consecutiveNormals = 0;
                break;
        }

        return new MonitorEvaluation(
            status,
            transition,
            age,
            _consecutiveAlerts,
            _consecutiveNormals,
            _isAlerting);
    }

    private LogActivityStatus Classify(TimeSpan age)
    {
        if (age >= _thresholds.AlertAfter)
        {
            return LogActivityStatus.Alert;
        }

        if (age >= _thresholds.WarningAfter)
        {
            return LogActivityStatus.Warning;
        }

        return LogActivityStatus.Normal;
    }
}
