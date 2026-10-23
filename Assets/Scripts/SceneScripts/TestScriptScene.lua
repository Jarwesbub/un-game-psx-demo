function onCreate(self)
    Debug.Log("GAME LOADED!!!!")
end

function onButtonPress(self, button)
    Debug.Log("BUTTON PRESSED!!!")
    if button == Input.START then
        -- Start game.
        --Audio.PauseCDDA()
        --Scene.Load(1)
    end
end
