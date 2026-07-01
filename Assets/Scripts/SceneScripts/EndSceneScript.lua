function onCreate(self)
    Camera.FollowPsxPlayer(false)
    local pos = Vec3.new(0, 0, 0)
    setCameraPosition(pos, pos)
end

function onButtonPress(self, button)
    if button == Input.SELECT then
        -- Return to main menu.
        Scene.Load(0)
    end
end
