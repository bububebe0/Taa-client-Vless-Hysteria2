    private static void ApplyDarkTheme()
    {
        const string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#E2E8F0'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
            <Border x:Name='box' Width='18' Height='18' Background='#0D1526'
                    BorderBrush='#1A2640' BorderThickness='1.5' CornerRadius='5' VerticalAlignment='Center'>
              <Path x:Name='chk' Visibility='Collapsed' Data='M 2.5,9 L 7,13.5 L 15.5,4.5'
                    Stroke='#FFFFFF' StrokeThickness='2' HorizontalAlignment='Center' VerticalAlignment='Center'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>
            </Border>
            <ContentPresenter Margin='9,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='chk' Property='Visibility' Value='Visible'/>
              <Setter TargetName='box' Property='Background' Value='#6366F1'/>
              <Setter TargetName='box' Property='BorderBrush' Value='#6366F1'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='box' Property='Opacity' Value='0.35'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBoxItem'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#E2E8F0'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='bd' Background='{TemplateBinding Background}' Padding='11,8,11,8' CornerRadius='6' Margin='3,1,3,1'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#1A2347'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#1E2F5A'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Background' Value='#0D1526'/>
    <Setter Property='Foreground' Value='#E2E8F0'/>
    <Setter Property='BorderBrush' Value='#1A2640'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='MaxDropDownHeight' Value='280'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <Border x:Name='bg' Background='{TemplateBinding Background}'
                    BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}'
                    CornerRadius='9'/>
            <ContentPresenter x:Name='cp'
                              Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                              Margin='12,0,32,0'
                              VerticalAlignment='Center'
                              HorizontalAlignment='Left'
                              IsHitTestVisible='False'/>
            <ToggleButton Background='Transparent' BorderThickness='0' Cursor='Hand'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Grid>
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition/>
                      <ColumnDefinition Width='30'/>
                    </Grid.ColumnDefinitions>
                    <Rectangle Grid.ColumnSpan='2' Fill='Transparent'/>
                    <Path Grid.Column='1' Data='M 0,0 L 4,4.5 L 8,0'
                          Stroke='#4B5A7A' StrokeThickness='1.5'
                          VerticalAlignment='Center' HorizontalAlignment='Center'
                          StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                  </Grid>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <Popup x:Name='PART_Popup'
                   IsOpen='{TemplateBinding IsDropDownOpen}'
                   Placement='Bottom' AllowsTransparency='True' Focusable='False'>
              <Border Background='#0D1526' BorderBrush='#1A2640' BorderThickness='1' CornerRadius='10'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                      MaxHeight='{TemplateBinding MaxDropDownHeight}'
                      Margin='0,4,0,0'>
                <ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' Padding='4,4,4,4'>
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='bg' Property='Opacity' Value='0.4'/>
            </Trigger>
            <Trigger Property='IsDropDownOpen' Value='True'>
              <Setter TargetName='bg' Property='BorderBrush' Value='#6366F1'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TabControl'>
    <Setter Property='Background' Value='#0D1526'/>
    <Setter Property='BorderBrush' Value='#1A2640'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TabControl'>
          <Border CornerRadius='14' ClipToBounds='True'
                  BorderBrush='#1A2640' BorderThickness='0'>
            <Grid>
              <Grid.RowDefinitions>
                <RowDefinition Height='Auto'/>
                <RowDefinition Height='*'/>
              </Grid.RowDefinitions>
              <Border Grid.Row='0' Background='#07090F' BorderBrush='#1A2640'
                      BorderThickness='0,0,0,1'>
                <TabPanel x:Name='HeaderPanel' IsItemsHost='True' Background='Transparent' Margin='4,0,0,0'/>
              </Border>
              <Border Grid.Row='1' Background='#0D1526' BorderBrush='Transparent'
                      BorderThickness='0' CornerRadius='0,0,14,14'>
                <ContentPresenter ContentSource='SelectedContent' Margin='0'/>
              </Border>
            </Grid>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TabItem'>
    <Setter Property='Foreground' Value='#4B5A7A'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Padding' Value='18,14,18,14'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TabItem'>
          <Border x:Name='bd' Background='{TemplateBinding Background}'
                  Padding='{TemplateBinding Padding}' Cursor='Hand'>
            <Grid>
              <TextBlock x:Name='txt' Text='{TemplateBinding Header}'
                         FontSize='{TemplateBinding FontSize}'
                         VerticalAlignment='Center' FontWeight='Normal'>
                <TextBlock.Foreground>
                  <SolidColorBrush x:Name='TxtBrush' Color='#4B5A7A'/>
                </TextBlock.Foreground>
              </TextBlock>
              <Rectangle x:Name='indicator' Height='2' VerticalAlignment='Bottom'
                         Fill='Transparent' Margin='0,0,0,-1'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='txt' Property='FontWeight' Value='SemiBold'/>
              <Setter TargetName='indicator' Property='Fill' Value='#6366F1'/>
              <Trigger.EnterActions>
                <StopStoryboard BeginStoryboardName='SelExitSB'/>
                <StopStoryboard BeginStoryboardName='HoverEnterSB'/>
                <StopStoryboard BeginStoryboardName='HoverExitSB'/>
                <BeginStoryboard x:Name='SelEnterSB'>
                  <Storyboard FillBehavior='HoldEnd'>
                    <ColorAnimation Storyboard.TargetName='TxtBrush'
                                    Storyboard.TargetProperty='Color'
                                    To='#FFFFFF' Duration='0:0:0.2'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <StopStoryboard BeginStoryboardName='SelEnterSB'/>
                <BeginStoryboard x:Name='SelExitSB'>
                  <Storyboard FillBehavior='Stop'>
                    <ColorAnimation Storyboard.TargetName='TxtBrush'
                                    Storyboard.TargetProperty='Color'
                                    To='#4B5A7A' Duration='0:0:0.2'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <MultiTrigger>
              <MultiTrigger.Conditions>
                <Condition Property='IsMouseOver' Value='True'/>
                <Condition Property='IsSelected' Value='False'/>
              </MultiTrigger.Conditions>
              <MultiTrigger.EnterActions>
                <StopStoryboard BeginStoryboardName='HoverExitSB'/>
                <BeginStoryboard x:Name='HoverEnterSB'>
                  <Storyboard FillBehavior='HoldEnd'>
                    <ColorAnimation Storyboard.TargetName='TxtBrush'
                                    Storyboard.TargetProperty='Color'
                                    To='#8B9BB8' Duration='0:0:0.15'/>
                  </Storyboard>
                </BeginStoryboard>
              </MultiTrigger.EnterActions>
              <MultiTrigger.ExitActions>
                <StopStoryboard BeginStoryboardName='HoverEnterSB'/>
                <BeginStoryboard x:Name='HoverExitSB'>
                  <Storyboard FillBehavior='Stop'>
                    <ColorAnimation Storyboard.TargetName='TxtBrush'
                                    Storyboard.TargetProperty='Color'
                                    To='#4B5A7A' Duration='0:0:0.15'/>
                  </Storyboard>
                </BeginStoryboard>
              </MultiTrigger.ExitActions>
            </MultiTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='SelectionBrush' Value='#6366F1'/>
    <Setter Property='SelectionOpacity' Value='0.4'/>
    <Setter Property='CaretBrush' Value='#E2E8F0'/>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Background='Transparent' BorderThickness='0' Opacity='0'/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Background='Transparent' BorderThickness='0' Opacity='0'/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border x:Name='th' Background='#1E3050' CornerRadius='3' Margin='2,3,2,3'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='th' Property='Background' Value='#2A4070'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                          <Setter TargetName='th' Property='Background' Value='#6366F1'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Height' Value='6'/>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='Transparent'>
                <Track x:Name='PART_Track'>
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command='ScrollBar.PageLeftCommand' Background='Transparent' BorderThickness='0' Opacity='0'/>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command='ScrollBar.PageRightCommand' Background='Transparent' BorderThickness='0' Opacity='0'/>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb>
                      <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                          <Border x:Name='th' Background='#1E3050' CornerRadius='3' Margin='3,2,3,2'/>
                          <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                              <Setter TargetName='th' Property='Background' Value='#2A4070'/>
                            </Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

</ResourceDictionary>";
        try
        {
            var rd = (ResourceDictionary)XamlReader.Parse(xaml);
            Application.Current.Resources.MergedDictionaries.Add(rd);
        }
        catch (Exception ex)
        {
            try { Paths.AppendLog(Paths.LogPath("theme_error.log"), $"{DateTime.Now} - Theme error: {ex}\n"); } catch { }
        }
    }

    private ToggleButton MakeToggleSwitch(string text, bool isChecked, Action<bool> onChanged)
    {
        const string templateXaml = @"
<ControlTemplate TargetType='ToggleButton'
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='Auto'/>
      <ColumnDefinition Width='*'/>
    </Grid.ColumnDefinitions>
    <Border x:Name='Border' Width='40' Height='22' CornerRadius='11' BorderThickness='1' Grid.Column='0'>
      <Border.Background><SolidColorBrush x:Name='TrackBrush' Color='#162040'/></Border.Background>
      <Border.BorderBrush><SolidColorBrush x:Name='TrackBorderBrush' Color='#1A2640'/></Border.BorderBrush>
    </Border>
    <Border x:Name='Thumb' Width='16' Height='16' CornerRadius='8'
            Grid.Column='0' VerticalAlignment='Center' HorizontalAlignment='Left' Margin='3,0,0,0'>
      <Border.Background><SolidColorBrush x:Name='ThumbBrush' Color='#3A4A6A'/></Border.Background>
      <Border.RenderTransform><TranslateTransform x:Name='ThumbTranslate' X='0'/></Border.RenderTransform>
    </Border>
    <ContentPresenter Grid.Column='1' Content='{TemplateBinding Content}'
                      Margin='10,0,0,0' HorizontalAlignment='Left' VerticalAlignment='Center'/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter TargetName='Border' Property='Opacity' Value='0.4'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private static readonly string _btnXaml = @"
<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='bd' Background='{TemplateBinding Background}'
          BorderBrush='{TemplateBinding BorderBrush}'
          BorderThickness='{TemplateBinding BorderThickness}'
          CornerRadius='10'>
    <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter TargetName='bd' Property='Opacity' Value='0.38'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private static Button MakeBtn(string text, Color bg)
    {
        const string xaml = @"<ControlTemplate TargetType='Button'
            xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
            xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
          <Border x:Name='bd' Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}'
                  CornerRadius='6'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
        </ControlTemplate>";
        var btn = new Button
        {
            Content = text, Width = 100, Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(bg), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 13,
            Template = (ControlTemplate)XamlReader.Parse(xaml)
        };
        return btn;
    }
}

    private static readonly string _titleBtnXaml = @"
<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='bd' Background='{TemplateBinding Background}'>
    <TextBlock Text='{TemplateBinding Content}' HorizontalAlignment='Center' VerticalAlignment='Center'
               Foreground='{TemplateBinding Foreground}' FontSize='12' FontWeight='Normal'/>
  </Border>
</ControlTemplate>";
