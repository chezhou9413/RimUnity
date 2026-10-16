using System;
using TMPro;

namespace RimUnity.Editor
{
    //输入框只接通已实现的桌面标准单行编辑模式，其余设置明确阻止导出。
    internal static class TmpInputWriter
    {
        //避免密码、多行和自定义验证规则在序列化后被忽略。
        internal static void Validate(TMP_InputField input, string path)
        {
            bool supportedContent = input.contentType == TMP_InputField.ContentType.Standard
                || input.contentType == TMP_InputField.ContentType.IntegerNumber || input.contentType == TMP_InputField.ContentType.DecimalNumber;
            bool supportedValidation = input.characterValidation == TMP_InputField.CharacterValidation.None
                || input.characterValidation == TMP_InputField.CharacterValidation.Integer || input.characterValidation == TMP_InputField.CharacterValidation.Decimal;
            if (!supportedContent || input.inputType != TMP_InputField.InputType.Standard
                || input.lineType != TMP_InputField.LineType.SingleLine || !supportedValidation
                || input.inputValidator != null || input.verticalScrollbar != null || input.onTouchScreenKeyboardStatusChanged.GetPersistentEventCount() > 0)
                throw new NotSupportedException(path + "：TMP 输入框支持标准、整数和小数单行模式，不支持自定义校验和移动软键盘事件。");
            if (input.textComponent == null || input.textViewport == null)
                throw new InvalidOperationException(path + "：输入框必须指定文本组件和视口。");
        }
    }
}
