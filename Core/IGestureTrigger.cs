using System;

namespace Glix;

/// <summary>
/// 手势触发源：从原始鼠标输入流中识别并产出统一手势信号。
/// 旋转识别、方位热区识别等实现均可作为触发源接入 <see cref="GestureEngine"/>。
/// </summary>
internal interface IGestureTrigger : IDisposable
{
    /// <summary>
    /// 识别到手势信号时触发。处理逻辑应保持轻量，耗时操作需转发至 UI/工作线程。
    /// </summary>
    event Action<GestureSignal>? Signaled;

    /// <summary>
    /// 喂入一帧原始鼠标输入，由实现自行判断是否为一次手势。
    /// </summary>
    /// <param name="input">鼠标输入事件。</param>
    /// <param name="timestampMs">时间戳（毫秒）。</param>
    void Feed(MouseInput input, long timestampMs);

    /// <summary>
    /// 清空已累积的轨迹状态，用于按钮状态变化或上下文切换时打断识别。
    /// </summary>
    void Reset();
}

/// <summary>
/// 自门控触发源：直接接收未经区域、抖动与硬屏蔽过滤的原始位移，由自身负责上下文判定与中断清理。
/// </summary>
/// <remarks>
/// 供需要独立区域规则与冷却的手势接入，避免改动既有旋转手势的编排逻辑；移除该接口实现即可整体撤掉对应功能。
/// </remarks>
internal interface ISelfGatedTrigger : IGestureTrigger
{
    /// <summary>
    /// 接收每一帧原始鼠标位移。
    /// </summary>
    /// <param name="input">鼠标输入事件。</param>
    /// <param name="timestampMs">时间戳（毫秒）。</param>
    void FeedRaw(MouseInput input, long timestampMs);
}
