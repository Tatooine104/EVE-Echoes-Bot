#include "..\Resource\libUtility.au3"

Opt("WinTitleMatchMode", 2)

Global $lDebug = True

$hWindow = _GetHandleByTitle("MEmu W.01")

If @error Then
    ; Сюда мы попадем, потому что функция выполнила SetError(1, ...)
    _CW("Ошибка! Окно не найдено. Код ошибки: " & @error)
Else
    _CW("Окно найдено, хендл: " & $hWindow)
EndIf