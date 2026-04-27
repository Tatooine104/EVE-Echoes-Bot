; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer (Image Library)                                                                       #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.25                                                                                                 #
; # FILENAME.......: libImageSearch.au3                                                                                         #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Библиотека функций для работы с графическими образами.                                                     #
; #                                                                                                                             #
; # FUNCTIONS......: _MyImageSearch         - Поиск в массиве области с логированием и скриншотом.                              #
; #                  _ImageSearch           - Поиск изображения по всему рабочему столу.                                        #
; #                  _ImageSearchClientArea - Поиск строго внутри клиентской части окна (без рамок).                            #
; #                  _ImageSearchArea       - Базовая обертка для прямого вызова ImageSearchDLL.dll.                            #
; #                  _WaitForImageSearch    - Цикличное ожидание появления одной картинки (тайм-аут).                           #
; #                  _WaitForImagesSearch   - Ожидание появления любой картинки из списка (массива).                            #
; #                  _WinGetClientPos       - Получение экранных координат клиентской области (WinAPI).                         #
; #                  _FindAndClick          - Найти изображение инажать на него.                                                #
; #                                                                                                                             #
; ###############################################################################################################################

#include-once ; Добавить в первую строку файла библиотеки

#include <ScreenCapture.au3>
#include <libUtility.au3>
#include <Constants.au3>
#include <GDIPlus.au3>

Global $g_adbPath = "C:\Program Files\Microvirt\MEmu\adb.exe"

; ###############################################################################################################################
; # FUNCTION.......: _Get_Screenshot_By_ID                                                                                      #
; # PARAMETERS.....: $sDeviceID - ADB адрес ("127.0.0.1:21503")                                                                 #
; # RETURN.........: String - Путь к BMP, который НУЖНО УДАЛИТЬ после использования, или "" при ошибке                          #
; ###############################################################################################################################
Func _Get_Screenshot_By_ID($sDeviceID)
    Local $sSafeName = StringRegExpReplace($sDeviceID, "[^a-zA-Z0-9]", "_")
    Local $sWorkDir  = @ScriptDir & "\screenshots"
    Local $sTempPng  = $sWorkDir & "\~" & $sSafeName & ".png"
    Local $sFinalBmp = $sWorkDir & "\snap_" & $sSafeName & ".bmp"

    If Not FileExists($sWorkDir) Then DirCreate($sWorkDir)

    ; 1. Захват PNG
    RunWait('"' & $g_adbPath & '" -s ' & $sDeviceID & ' shell screencap -p /sdcard/screen.png', "", @SW_HIDE)
    Local $iPull = RunWait('"' & $g_adbPath & '" -s ' & $sDeviceID & ' pull /sdcard/screen.png "' & $sTempPng & '"', "", @SW_HIDE)

    If Not FileExists($sTempPng) Then Return ""

    ; 2. Конвертация в 24-bit BMP
    _GDIPlus_Startup()
    Local $hImage = _GDIPlus_ImageLoadFromFile($sTempPng)
    Local $bSave  = _GDIPlus_ImageSaveToFile($hImage, $sFinalBmp)
    _GDIPlus_ImageDispose($hImage)
    _GDIPlus_Shutdown()
    
    ; Очистка временного PNG сразу после конвертации
    FileDelete($sTempPng) 

    Return $bSave ? $sFinalBmp : ""
EndFunc

; ###############################################################################################################################
; # FUNCTION.......: _MyWaitForImageSearch
; # DESCRIPTION....: Ожидает появления изображения, обновляя скриншот ADB в каждом цикле.
; # PARAMETERS ....: ..., $sDeviceID - ID эмулятора для снятия новых скриншотов.
; ###############################################################################################################################
Func _MyWaitForImageSearch($sImgName, $sResDir, $aRect, $iWaitSecs, ByRef $x, ByRef $y, $iTolerance, $sDeviceID)
    ; 1. Проверка входных данных
    If Not IsArray($aRect) Or UBound($aRect) < 4 Then Return 0
    
    Local $iWaitMs = $iWaitSecs * 1000
    Local $hTimer = TimerInit()
    Local $sCurrentScreen = ""
    
    _Log("Ожидание '" & $sImgName & "' на устройстве " & $sDeviceID & " (" & $iWaitSecs & " сек.)")

    While TimerDiff($hTimer) < $iWaitMs
        ; ШАГ А: Делаем свежий снимок экрана
        $sCurrentScreen = _Get_Screenshot_By_ID($sDeviceID)
        
        If $sCurrentScreen <> "" Then
            ; ШАГ Б: Ищем на этом снимке
            ; (Добавляем параметр $sCurrentScreen в вызов _MyImageSearch)
            Local $iFound = _MyImageSearch($sImgName, $sResDir, $aRect, $x, $y, $iTolerance, $sCurrentScreen)
            
            ; ШАГ В: Сразу удаляем скриншот
            FileDelete($sCurrentScreen)
            
            If $iFound Then 
                _Log("Изображение '" & $sImgName & "' найдено.")
                Return 1
            EndIf
        EndIf
        
        Sleep(200) ; Пауза между попытками (для ADB лучше 200мс+, чтобы не перегружать поток)
    WEnd

    _Log("Тайм-аут: '" & $sImgName & "' не появилось.")
    Return 0
EndFunc   ;==>_MyWaitForImageSearch




; ###############################################################################################################################
; # FUNCTION.......: _FindAndClick
; # DESCRIPTION....: Ищет изображение в BMP-файле и выполняет ADB-клик при обнаружении.
; # PARAMETERS ....: ..., $sDeviceID - ID эмулятора для клика, $sSourceBmp - путь к актуальному скриншоту.
; ###############################################################################################################################
Func _FindAndClick($sImg, $sResDir, $aRect, $sDeviceID, $sSourceBmp)
    Local $x, $y
    
    ; 1. Ищем изображение на конкретном скриншоте
    ; Используем допуск 10 (как в базовых настройках) или передаем свой
    If _MyImageSearch($sImg, $sResDir, $aRect, $x, $y, 10, $sSourceBmp) Then
        _Log("_FindAndClick: Найдено '" & $sImg & "' в " & $sDeviceID & ", выполняем клик.")
        
        _HumanSleep(100, 300) ; Пауза для имитации действий человека
        
        ; 2. КЛИК ЧЕРЕЗ ADB (работает в фоне, не трогает курсор Windows)
        ; Используем глобальный путь к ADB из ScreenshotMaker.au3
        Local $sCmd = '"' & $g_adbPath & '" -s ' & $sDeviceID & ' shell input tap ' & $x & ' ' & $y
        RunWait($sCmd, "", @SW_HIDE)
        
        Return True
    EndIf
    
    _Log("_FindAndClick: '" & $sImg & "' не найдено на текущем снимке.")
    Return False
EndFunc   ;==>_FindAndClick




; ###############################################################################################################################
; # FUNCTION.......: _MyImageSearch
; # DESCRIPTION....: Ищет изображение внутри заранее подготовленного BMP-скриншота (ADB).
; # PARAMETERS ....: $sImgName   - Имя искомого файла ("button.bmp").
; #                  $sResDir    - Папка с ресурсами.
; #                  $aRect      - Область поиска [X1, Y1, X2, Y2].
; #                  $x, $y      - [ByRef] Координаты результата.
; #                  $iTolerance - Допуск (0-255).
; #                  $sSourceBmp - ПУТЬ К СКРИНШОТУ, полученному через ADB.
; # RETURN.........: 1 - Найдено, 0 - Не найдено.
; ###############################################################################################################################
Func _MyImageSearch($sImgName, $sResDir, $aRect, ByRef $x, ByRef $y, $iTolerance, $sSourceBmp)
    ; 1. Валидация входных данных
    If Not IsArray($aRect) Or UBound($aRect) < 4 Then 
        _CW("_MyImageSearch: Ошибка массива координат для " & $sImgName & @CRLF)
        Return 0
    EndIf
    
    If Not FileExists($sSourceBmp) Then
        _CW("_MyImageSearch: Ошибка - файл скриншота не найден: " & $sSourceBmp & @CRLF)
        Return 0
    EndIf

    ; 2. Формируем путь к эталону
    If $sResDir <> "" And StringRight($sResDir, 1) <> "\" Then $sResDir &= "\"
    Local $sPatternPath = $sResDir & $sImgName

    ; 3. ПОИСК (Используем ImageSearchArea, передавая путь к исходному скриншоту)
    ; Примечание: В зависимости от вашей версии DLL, путь к источнику ($sSourceBmp) 
    ; может передаваться последним параметром или через доп. функции.
    ; Стандартный вызов для поиска в файле:
    Local $iResult = _ImageSearchArea($sPatternPath, 1, $aRect[0], $aRect[1], $aRect[2], $aRect[3], $x, $y, $iTolerance, $sSourceBmp)
    
    ; 4. Обработка промаха (Логирование)
    If $iResult = 0 Then
        Local $sLogDir = @ScriptDir & "\Logs"
        If Not FileExists($sLogDir) Then DirCreate($sLogDir)
        
        Local $sTime = @HOUR & @MIN & @SEC
        Local $sLogFile = $sLogDir & "\ERR_" & $sTime & "_" & $sImgName
        
        ; Вместо снятия нового скриншота (который может уже измениться),
        ; просто копируем тот ADB-скрин, на котором не нашли картинку.
        FileCopy($sSourceBmp, $sLogFile, 8) 
        _CW("[-] Не нашли " & $sImgName & ". Скриншот сохранен в Logs." & @CRLF)
    EndIf

    Return $iResult
EndFunc





; ###############################################################################################################################
; # FUNCTION.......: _ImageSearch
; # DESCRIPTION....: Ищет изображение во всем файле скриншота (от 0,0 до ширины/высоты BMP).
; # PARAMETERS ....: $findImage      - Путь к эталону (что ищем).
; #                  $resultPosition - 0 (угол) или 1 (центр).
; #                  $x, $y          - [ByRef] Координаты результата.
; #                  $tolerance      - Допуск (0-255).
; #                  $sSourceBmp     - ПУТЬ К ФАЙЛУ СКРИНШОТА (из которого ищем).
; # RETURN.........: 1 - Найдено, 0 - Не найдено.
; ###############################################################################################################################
Func _ImageSearch($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $sSourceBmp = "")
    
    ; Если путь к скриншоту не передан, функция не сможет работать в режиме ADB
    If $sSourceBmp = "" Or Not FileExists($sSourceBmp) Then
        _CW("!!! _ImageSearch ERROR: Не указан файл источника для поиска " & $findImage & @CRLF)
        Return 0
    EndIf

    ; Получаем размер скриншота, чтобы знать границы поиска
    _GDIPlus_Startup()
    Local $hImg = _GDIPlus_ImageLoadFromFile($sSourceBmp)
    Local $iW = _GDIPlus_ImageGetWidth($hImg)
    Local $iH = _GDIPlus_ImageGetHeight($hImg)
    _GDIPlus_ImageDispose($hImg)
    _GDIPlus_Shutdown()

    ; Вызываем поиск в области, ограниченной размерами самого скриншота
    ; Обратите внимание: $sSourceBmp передается в конец функции _ImageSearchArea
    Return _ImageSearchArea($findImage, $resultPosition, 0, 0, $iW, $iH, $x, $y, $tolerance, $sSourceBmp)

EndFunc   ;==>_ImageSearch




; ###############################################################################################################################
; # FUNCTION.......: _ImageSearchClientArea
; # DESCRIPTION....: В режиме ADB эквивалентна поиску по всему скриншоту, так как рамок Windows нет.
; # PARAMETERS ....: ..., $sSourceBmp - путь к актуальному скриншоту ADB.
; ###############################################################################################################################
Func _ImageSearchClientArea($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $sSourceBmp)
    
    ; В ADB скриншоте нет рамок, поэтому клиентская область = весь файл.
    ; Мы просто вызываем нашу обновленную функцию _ImageSearch.
    
    Return _ImageSearch($findImage, $resultPosition, $x, $y, $tolerance, $sSourceBmp)

EndFunc   ;==>_ImageSearchClientArea




; ###############################################################################################################################
; # FUNCTION.......: _ImageSearchArea
; # DESCRIPTION....: Базовая функция. Теперь умеет искать как на экране, так и ВНУТРИ BMP-файла (ADB).
; # PARAMETERS ....: ..., $hSource - [Optional] ПУТЬ К BMP-ФАЙЛУ (скриншоту ADB). Если 0 - ищет на экране.
; ###############################################################################################################################
Func _ImageSearchArea($findImage, $resultPosition, $x1, $y1, $right, $bottom, ByRef $x, ByRef $y, $tolerance, $hSource = 0)

    ; 1. Путь к DLL
    Local $sResPath = IsDeclared("sResourceDir") ? Eval("sResourceDir") : @ScriptDir & "\"
    Local $sDllPath = $sResPath & "ImageSearchDLL.dll"
    
    If Not FileExists($sDllPath) Then
        _CW("КРИТИЧЕСКАЯ ОШИБКА: DLL не найдена: " & $sDllPath & @CRLF)
        Return 0
    EndIf

    ; 2. Подготовка строки поиска
    Local $sSearchStr = $findImage
    If $tolerance > 0 Then $sSearchStr = "*" & $tolerance & " " & $findImage
    
    ; --- НОВЫЙ БЛОК: Поиск в файле (ADB режим) ---
    Local $result
    If $hSource <> 0 And FileExists($hSource) Then
        ; Если передан путь к файлу, используем вызов "ImageSearchOnImage" 
        ; (Большинство современных ImageSearchDLL поддерживают этот метод для фонового поиска)
        $result = DllCall($sDllPath, "str", "ImageSearchOnImage", "str", $hSource, "int", $x1, "int", $y1, "int", $right, "int", $bottom, "str", $sSearchStr)
    Else
        ; Стандартный поиск по всему экрану (если файл не передан)
        $result = DllCall($sDllPath, "str", "ImageSearch", "int", $x1, "int", $y1, "int", $right, "int", $bottom, "str", $sSearchStr)
    EndIf
    ; ----------------------------------------------

    If @error Or Not IsArray($result) Or $result[0] = "0" Then Return 0

    ; 3. Разбор результата
    Local $array = StringSplit($result[0], "|")
    If $array[0] < 3 Then Return 0

    $x = Int(Number($array[2]))
    $y = Int(Number($array[3]))
    
    If $resultPosition = 1 Then
        $x = $x + Int(Number($array[4]) / 2)
        $y = $y + Int(Number($array[5]) / 2)
    EndIf
    
    ; В ADB режиме ($hSource <> 0) нам НЕ НУЖНО вычитать координаты окна _WinGetClientPos, 
    ; так как координаты в файле всегда начинаются с 0,0 (это чистый Android).
    
    Return 1
EndFunc   ;==>_ImageSearchArea




; ###############################################################################################################################
; # FUNCTION.......: _WaitForImageSearch
; # DESCRIPTION....: Ожидает появления изображения, обновляя скриншот ADB в каждом цикле.
; # PARAMETERS ....: ..., $sDeviceID - ID эмулятора (напр. "127.0.0.1:21503")
; ###############################################################################################################################
Func _WaitForImageSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $sDeviceID)

	Local $iWaitMs = $waitSecs * 1000
	Local $hTimer = TimerInit()
    Local $sCurrentFile = ""
    
    _CW("--> Ожидание появления: " & $findImage & " (" & $waitSecs & " сек)" & @CRLF)

	While TimerDiff($hTimer) < $iWaitMs
        ; 1. Получаем СВЕЖИЙ скриншот в каждом цикле
        $sCurrentFile = _Get_Screenshot_By_ID($sDeviceID)
        
        If $sCurrentFile <> "" Then
            ; 2. Ищем на этом скриншоте (передаем путь к файлу вместо hwnd)
            Local $iResult = _ImageSearch($findImage, $resultPosition, $x, $y, $tolerance, $sCurrentFile)
            
            ; 3. УДАЛЯЕМ скриншот сразу после поиска
            FileDelete($sCurrentFile)
            
            ; 4. Если нашли - выходим из функции с успехом
            If $iResult > 0 Then 
                _CW("--- Найдено! ---" & @CRLF)
                Return 1
            EndIf
        EndIf
        
		Sleep(250) ; Пауза между попытками (чуть больше для ADB, чтобы не "забить" канал связи)
	WEnd

    _CW("!!! Тайм-аут: " & $findImage & " не обнаружено." & @CRLF)
	Return 0
    
EndFunc   ;==>_WaitForImageSearch




; ###############################################################################################################################
; # FUNCTION.......: _WaitForImagesSearch
; # DESCRIPTION....: Ожидает появления одного из нескольких изображений на одном ADB-скриншоте.
; # PARAMETERS ....: ..., $sDeviceID - ID эмулятора.
; # RETURN.........: Индекс найденной картинки (1, 2...) или 0 (тайм-аут).
; ###############################################################################################################################
Func _WaitForImagesSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $sDeviceID)
    
    Local $iWaitMs = $waitSecs * 1000
    Local $hTimer = TimerInit()
    Local $sCurrentFile = ""
    
    _CW("--> Ожидание группы изображений (" & $findImage[0] & " шт.) на " & $sDeviceID & @CRLF)

    While TimerDiff($hTimer) < $iWaitMs
        ; 1. ДЕЛАЕМ ОДИН СКРИНШОТ ДЛЯ ВСЕГО МАССИВА КАРТИНОК
        $sCurrentFile = _Get_Screenshot_By_ID($sDeviceID)
        
        If $sCurrentFile <> "" Then
            ; 2. ПЕРЕБИРАЕМ КАРТИНКИ, ИСПОЛЬЗУЯ ЭТОТ ЖЕ ФАЙЛ
            For $i = 1 To $findImage[0]
                Local $iResult = _ImageSearch($findImage[$i], $resultPosition, $x, $y, $tolerance, $sCurrentFile)
                
                If $iResult > 0 Then
                    _CW("--- Найдено изображение индекс: [" & $i & "] ---" & @CRLF)
                    FileDelete($sCurrentFile) ; Удаляем перед выходом
                    Return $i
                EndIf
            Next
            
            ; 3. ЕСЛИ НИЧЕГО НЕ НАШЛИ - УДАЛЯЕМ СКРИН И ИДЕМ НА НОВЫЙ КРУГ
            FileDelete($sCurrentFile)
        EndIf
        
        Sleep(200) ; Пауза между обновлениями экрана
    WEnd

    _CW("!!! Тайм-аут ожидания группы изображений." & @CRLF)
    Return 0

EndFunc   ;==>_WaitForImagesSearch




; #FUNCTION# ====================================================================================================================
; Name...........: _WinGetClientPos
; Description....: Получает экранные координаты и размеры клиентской области окна (без рамок и заголовка).
; Syntax.........: _WinGetClientPos($hwnd)
; Parameters ....: $hwnd - Дескриптор окна (HWND).
; Return values .: Массив: [0] = X, [1] = Y (экранные), [2] = Ширина, [3] = Высота.
;                  При ошибке возвращает 0 и устанавливает @error = 1.
; Updated .......: 2026.04.25
; Version .......: 1.00
; Remarks .......: Использует WinAPI функцию ClientToScreen через user32.dll для точности.
; ===============================================================================================================================
Func _WinGetClientPos($hwnd)
    
    Local $aSize = WinGetClientSize($hwnd)
    If Not IsArray($aSize) Then Return SetError(1, 0, 0)

    ; Описываем структуру напрямую (POINT)
    Local $tPoint = DllStructCreate("long X;long Y")
    
    ; Устанавливаем начальную точку (0,0) — это левый верхний угол клиентской области относительно самого окна
    DllStructSetData($tPoint, "X", 0)
    DllStructSetData($tPoint, "Y", 0)

    ; Вызываем системную функцию для перевода клиентских 0,0 в экранные координаты
    Local $aRet = DllCall("user32.dll", "bool", "ClientToScreen", "hwnd", $hwnd, "ptr", DllStructGetPtr($tPoint))
    
    If @error Or Not $aRet[0] Then
        Return SetError(1, 0, 0)
    EndIf

    ; Формируем массив: [0]=X, [1]=Y, [2]=Ширина, [3]=Высота
    Local $aPos[4]
    $aPos[0] = DllStructGetData($tPoint, "X") ; Экранный X начала клиентской области
    $aPos[1] = DllStructGetData($tPoint, "Y") ; Экранный Y начала клиентской области
    $aPos[2] = $aSize[0]                      ; Ширина области
    $aPos[3] = $aSize[1]                      ; Высота области

    Return $aPos

EndFunc   ;==>_WinGetClientPos
