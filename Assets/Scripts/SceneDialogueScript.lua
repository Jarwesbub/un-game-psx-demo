-- Attach this script to the PSXSceneExporter!

local dialogueCanvas = -1
local dialogueText = -1
local inDialogue = false
local dialogueLines = {}
local dialogueLine = 0

function onSceneCreationEnd()
    dialogueCanvas = UI.FindCanvas("Dialogue")
    if dialogueCanvas >= 0 then
        dialogueText = UI.FindElement(dialogueCanvas, "DialogueText")
        UI.SetCanvasVisible(dialogueCanvas, false)
    end
end

function startDialogue(lines)
    if inDialogue then return end
    inDialogue = true
    dialogueLines = lines
    dialogueLine = 1
    Controls.SetEnabled(false)
    UI.SetCanvasVisible(dialogueCanvas, true)
    UI.SetText(dialogueText, dialogueLines[1])
end

function advanceDialogue()
    if not inDialogue then return end
    dialogueLine = dialogueLine + 1
    if dialogueLine > #dialogueLines then
        endDialogue()
    else
        UI.SetText(dialogueText, dialogueLines[dialogueLine])
    end
end

function endDialogue()
    inDialogue = false
    Controls.SetEnabled(true)
    UI.SetCanvasVisible(dialogueCanvas, false)
end

function isInDialogue()
    return inDialogue
end

-- Publish
_G.startDialogue = startDialogue
_G.advanceDialogue = advanceDialogue
_G.endDialogue = endDialogue
_G.isInDialogue = isInDialogue
