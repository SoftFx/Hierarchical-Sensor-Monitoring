namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// Optional sensor capability: change the sensor's description after it was created (#1482, the
    /// managed counterpart of the native <c>hsm_sensor_set_description</c>). Every sensor the collector
    /// creates implements it. It is kept separate from the sensor interfaces (<see cref="IInstantValueSensor{T}"/>,
    /// <see cref="IBarSensor{T}"/>, ...) so external implementations of those interfaces stay
    /// source-compatible; callers normally use the <c>SetDescription</c> extension methods in
    /// <see cref="HSMDataCollector.Core.SensorDescriptionExtensions"/>.
    /// </summary>
    public interface IDescribableSensor
    {
        /// <summary>
        /// Replaces the sensor's description and re-registers the sensor. Before the collector starts
        /// (and while it is stopped) the new text is what the next Start registers; while the collector
        /// is starting or running the sensor is re-registered at once — on every call, also when the
        /// text is unchanged. While the collector is stopping or after it was disposed nothing is sent.
        /// <c>null</c> is sent as a null description, which the server reads as "unchanged" (so it
        /// clears only a description the server has not received yet); pass <c>""</c> to clear a
        /// description the server already has. Callable from any thread; never throws.
        /// A sensor the collector does not hold is left unchanged and nothing is sent: one the collector
        /// rejected at creation (created while it was stopping; that handle is inert) or one removed
        /// from it. The native collector has the same rule in the form its API allows: it returns no
        /// handle for a rejected sensor, so <c>hsm_sensor_set_description</c> can only fail with
        /// <c>HSM_RESULT_INVALID_ARGUMENT</c> there. Disposing a sensor handle does not remove the sensor
        /// from the collector (it is registered again on the next Start), so it still accepts a new
        /// description.
        /// </summary>
        /// <param name="description">The new description text.</param>
        /// <returns><c>true</c> when the description was updated; <c>false</c> when the collector does
        /// not hold this sensor (nothing changed, nothing sent) or when the update failed (the failure is
        /// reported through the collector's error channel).</returns>
        bool SetDescription(string description);
    }
}
