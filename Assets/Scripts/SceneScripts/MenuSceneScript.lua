function onCreate()
    Persist.Set("came_from", 0)
    Persist.Set("has_key", 0) -- Reset cabin key pick.
end

function onButtonPress(self, button)
    if button == Input.START then
        -- Start game.
        Scene.Load(1)
    end
end
