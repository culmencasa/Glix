namespace Glix;

/// <summary>
/// 手势动作：消费 <see cref="GestureSignal"/> 并执行具体行为（导航、弹出层等）。
/// 一个信号可被多个动作处理，由 <see cref="CanHandle"/> 决定各自是否响应。
/// </summary>
internal interface IGestureAction
{
    /// <summary>
    /// 判断该动作是否能处理给定信号。
    /// </summary>
    /// <param name="signal">待处理手势信号。</param>
    /// <returns>能够处理返回 <c>true</c>。</returns>
    bool CanHandle(GestureSignal signal);

    /// <summary>
    /// 执行动作。方法内应自行处理跨线程、冷却与资源安全。
    /// </summary>
    /// <param name="signal">已确认由本动作处理的手势信号。</param>
    void Execute(GestureSignal signal);
}
