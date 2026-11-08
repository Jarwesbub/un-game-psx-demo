-- Attach this script to the PSXSceneExporter!

local dialogueCanvas = -1
local dialogueText = -1
local inDialogue = false
local dialogueLines = {}
local dialogueLine = 0

local pauseCanvas = -1
local inPauseScreen = false

function onSceneCreationEnd()
    dialogueCanvas = UI.FindCanvas("Dialogue")
    if dialogueCanvas >= 0 then
        dialogueText = UI.FindElement(dialogueCanvas, "DialogueText")
        UI.SetCanvasVisible(dialogueCanvas, false)
    end

    pauseCanvas = UI.FindCanvas("Pause")
    if pauseCanvas >= 0 then
        UI.SetCanvasVisible(pauseCanvas, false)
    end
end

-- DIALOGUE:

function startDialogue(lines)
    if inDialogue then return end
    onDisablePlayerInput() -- Disable player input in TankControlsScript.lua
    inDialogue = true
    dialogueLines = lines
    dialogueLine = 1
    Controls.SetEnabledPlayer1(false)
    UI.SetCanvasVisible(dialogueCanvas, true)
    UI.SetText(dialogueText, dialogueLines[1])
end

function advanceDialogue()
    if not inDialogue then return end
    SkinnedAnim.Stop("PlayerModel") -- Hotfix: Stop player walk animation on interact.
    dialogueLine = dialogueLine + 1
    if dialogueLine > #dialogueLines then
        endDialogue()
    else
        UI.SetText(dialogueText, dialogueLines[dialogueLine])
    end
end

function endDialogue()
    inDialogue = false
    UI.SetCanvasVisible(dialogueCanvas, false)
    onEnablePlayerInput() -- Enable player input in TankControlsScript.lua
end

function isInDialogue()
    return inDialogue
end

-- Publish
_G.startDialogue = startDialogue
_G.advanceDialogue = advanceDialogue
_G.endDialogue = endDialogue
_G.isInDialogue = isInDialogue


-- PAUSE SCREEN:

function openPauseScreen()
    inPauseScreen = true
    UI.SetCanvasVisible(pauseCanvas, true)
end

function closePauseScreen()
    inPauseScreen = false
    UI.SetCanvasVisible(pauseCanvas, false)
end

function isInPauseScreen()
    return inPauseScreen
end

-- Publish
_G.openPauseScreen = openPauseScreen
_G.closePauseScreen = closePauseScreen
_G.isInPauseScreen = isInPauseScreen

-- UI check:
function isUIActive()
    return inDialogue or inPauseScreen
end

_G.isUIActive = isUIActive
