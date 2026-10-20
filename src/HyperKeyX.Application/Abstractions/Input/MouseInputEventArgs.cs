namespace HyperKeyX.Application.Abstractions.Input;

public sealed class MouseInputEventArgs : EventArgs
{
    public MouseInputEventArgs(MouseInputEvent input)
    {
        Input = input;
    }

    public MouseInputEvent Input { get; }
    public bool SuppressOriginalInput { get; set; }
}
