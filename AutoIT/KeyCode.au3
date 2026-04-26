_Log("Нажимай клавиши (Esc для выхода)...")

While 1
    For $i = 1 To 255
        If _IsPressed(StringFormat("%02X", $i)) Then
            Local $iScanCode = _GetScanCode($i)
            Local $sHexCode = StringFormat("%03X", $iScanCode)
            Local $sKeyName = _GetKeyName($iScanCode) ; Получаем название

            _Log("Клавиша: [" & $sKeyName & "] -> Код для Send: {SC" & $sHexCode & "}")

            ; Ждем, пока отпустишь клавишу, чтобы не спамить
            Do
                Sleep(10)
            Until Not _IsPressed(StringFormat("%02X", $i))
        EndIf
    Next
    If _IsPressed("1B") Then ExitLoop ; Esc
    Sleep(10)
WEnd

; --- Функции для работы с API ---

Func _GetScanCode($vKey)
    Local $aRet = DllCall("user32.dll", "int", "MapVirtualKey", "int", $vKey, "int", 0)
    Return $aRet[0]
EndFunc

Func _GetKeyName($iScanCode)
    ; Сдвигаем скан-код для корректной работы API
    Local $lParam = BitShift($iScanCode, -16)
    Local $sName = DllStructCreate("char[64]")
    DllCall("user32.dll", "int", "GetKeyNameTextA", "long", $lParam, "ptr", DllStructGetPtr($sName), "int", 64)
    Return DllStructGetData($sName, 1)
EndFunc

Func _IsPressed($sHexKey)
    Local $aRet = DllCall("user32.dll", "int", "GetAsyncKeyState", "int", '0x' & $sHexKey)
    Return BitAND($aRet[0], 0x8000) <> 0
EndFunc

Func _Log($sText)
    ConsoleWrite(BinaryToString(StringToBinary($sText & @CRLF, 4), 1))
EndFunc