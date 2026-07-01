function onCreate(self)
    Camera.FollowPsxPlayer(false)
    local pos = Vec3.new(0, 0, 0)
    setCameraPosition(pos, pos)
    -- Play background music.
    Audio.PlayCDDA(4, 1)
end

function onButtonPress(self, button)
    if button == Input.SELECT then
        -- Return to main menu.
        Audio.PauseCDDA()
        Scene.Load(0)
    end
end
