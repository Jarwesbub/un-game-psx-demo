function onInteract(self)
    local currentScene = Scene.GetIndex()

    if currentScene == 0 then -- Enter cabin.
        Persist.Set("came_from", 0)
        Scene.Load(1)
    end
    if currentScene == 1 then -- Get out of cabin.
        Persist.Set("came_from", 1)
        Scene.Load(0)
    end
end
