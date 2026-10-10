namespace RobleysQuestingFramework
{
    /// <summary>Reject duplicate samples and require three consecutive loaded, empty observations.</summary>
    public class ClearConfirmation
    {
        float _lastSample;
        bool _hasSample;
        int _emptySamples;

        public bool Observe(float time, bool ready, ClearAreaStatus status)
        {
            if (_hasSample && time - _lastSample >= 0 && time - _lastSample < 1f)
            {
                return false;
            }
            if (_hasSample && (time < _lastSample || time - _lastSample > 5f))
            {
                _emptySamples = 0;
            }
            _hasSample = true;
            _lastSample = time;
            _emptySamples = ready && status.IsClear ? _emptySamples + 1 : 0;
            return ready && (!status.IsClear || _emptySamples >= 3);
        }
    }
}
