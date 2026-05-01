local camRotStep = FixedPoint.new(1) / 100


function onUpdate(self, dt)
    local camRot = Camera.GetRotation()

    -- Camera rotation:
    if Input.IsHeld(Input.SQUARE) then
        Camera.SetRotation(Vec3.new(camRot.x, camRot.y - camRotStep, camRot.z))
        isInputHeld = true
    elseif Input.IsHeld(Input.CIRCLE) then
        Camera.SetRotation(Vec3.new(camRot.x, camRot.y + camRotStep, camRot.z))
        isInputHeld = true
    elseif Input.IsHeld(Input.TRIANGLE) then
        Camera.SetRotation(Vec3.new(camRot.x + camRotStep, camRot.y, camRot.z))
        isInputHeld = true
    elseif Input.IsHeld(Input.CROSS) then
        Camera.SetRotation(Vec3.new(camRot.x - camRotStep, camRot.y, camRot.z))
        isInputHeld = true
    end

    -- Camera FOV control:
    if Input.IsHeld(Input.R1) then
        local camFow = Camera.GetH()
        Camera.SetH(camFow + 1)
        Debug.Log("FOV: " .. camFow)
    elseif Input.IsHeld(Input.R2) then
        local camFow = Camera.GetH()
        Camera.SetH(camFow + -1)
        Debug.Log("FOV: " .. camFow)
    end
end

function onButtonRelease(self, button)
    if button == Input.SQUARE or button == Input.CIRCLE or button == Input.TRIANGLE or button == Input.CROSS then
        local newRot = Camera.GetRotation()
        Debug.Log("Camera rotation: " .. newRot.x .. ", " .. newRot.y .. ", " .. newRot.z)
    end
end
