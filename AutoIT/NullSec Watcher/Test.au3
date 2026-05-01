#include "..\Resource\libUtility.au3"

Opt("WinTitleMatchMode", 2)

Global $lDebug = True

$hWindow = _GetHandleByTitle("MEmu W.01")

If _MEmu_SetSize($hWindow, 1320, 760) Then
    _CW("Размер изменен успешно")
Else
    _CW("Ошибка: Не удалось изменить размер")
EndIf