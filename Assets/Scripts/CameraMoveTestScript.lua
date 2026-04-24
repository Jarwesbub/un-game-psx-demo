
local camRotStep = FixedPoint.new(1) / 64


function onUpdate(self, dt)
    local camRot = Camera.GetRotation()
    local isInputHeld = false

    if Input.IsHeld(Input.LEFT) then
        Camera.SetRotation(Vec3.new(camRot.x,camRot.y-camRotStep,camRot.z))
        isInputHeld=true
    elseif Input.IsHeld(Input.RIGHT) then
        Camera.SetRotation(Vec3.new(camRot.x,camRot.y+camRotStep,camRot.z))
        isInputHeld=true
    elseif Input.IsHeld(Input.UP) then
        Camera.SetRotation(Vec3.new(camRot.x+camRotStep,camRot.y,camRot.z))
        isInputHeld=true
    elseif Input.IsHeld(Input.DOWN) then
        Camera.SetRotation(Vec3.new(camRot.x-camRotStep,camRot.y,camRot.z))
        isInputHeld=true
    end

    if isInputHeld then
        local newRot = Camera.GetRotation()
        Debug.Log("Camera rot update: "..newRot.x..", "..newRot.y..", "..newRot.z)
    end

end
