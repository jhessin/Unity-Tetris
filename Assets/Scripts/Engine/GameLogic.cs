﻿using UnityEngine;
using TetrisEngine.TetriminosPiece;
using System.Collections.Generic;
using pooling;

namespace TetrisEngine
{
    //This class is responsable for conecting the engine to the view
    //It is also responsable for calling Playfield.Step
    public class GameLogic : MonoBehaviour
    {
        private const string JSON_PATH = @"Assets/SupportFiles/GameSettings.json";

        public GameObject tetriminoBlockPrefab;
        public Transform tetriminoParent;

        // [Header("This property will be overriten by GameSettings.json file.")]
        // [Space(-10)]
        // [Header("You can play with it while the game is in Play-Mode.")]

        private float timeToStep = 1f;

        [Header("Set rows cleared as game stages.")]
        public int[] stages = new int[3];
        private int currentLevel = 0;
        private GameSettings mGameSettings;
        private Playfield mPlayfield;
        private Wwise music;
        private List<TetriminoView> mTetriminos = new List<TetriminoView>();
        private float mTimer = 0f;

        [Space(10)]
        [SerializeField]
        protected bool enableStartScreen = true;
        private Pooling<TetriminoBlock> mBlockPool = new Pooling<TetriminoBlock>();
        private Pooling<TetriminoView> mTetriminoPool = new Pooling<TetriminoView>();

        private Tetrimino mCurrentTetrimino
        {
            get
            {
                return (mTetriminos.Count > 0 && !mTetriminos[mTetriminos.Count - 1].isLocked) ? mTetriminos[mTetriminos.Count - 1].currentTetrimino : null;
            }
        }

        private TetriminoView mPreview;
        private bool mRefreshPreview;
        private bool mGameIsOver = true;

        public void Start()
        {
            music = ScriptableObject.CreateInstance("Wwise") as Wwise;

            GameOver.instance.HideScreen(0f);
            LevelUp.instance.HideScreen(0f);
            StartGame.instance.ShowScreen(0f);
            Score.instance.HideScreen(0f);

            LoadSettings();
            ShowKeys();


            if (!enableStartScreen)
            {
                StartGame.instance.HideScreen(0f);
                Begin();
                return;
            }

        }

        private void ShowKeys()
        {
            string t = "";
            t += mGameSettings.rotateLeftKey.ToString() + " and ";
            t += mGameSettings.rotateRightKey.ToString() + " to rotate";
            t += System.Environment.NewLine;
            t += "Arrow keys to move";
            StartGame.instance.SetHelpText(t);
        }

        // Initiates pooling systems and playfield.
        public void Begin()
        {
            mBlockPool.createMoreIfNeeded = true;
            mBlockPool.Initialize(tetriminoBlockPrefab, null);

            mTetriminoPool.createMoreIfNeeded = true;
            mTetriminoPool.Initialize(new GameObject("BlockHolder", typeof(RectTransform)), tetriminoParent);
            mTetriminoPool.OnObjectCreationCallBack += x =>
            {
                x.OnDestroyTetrimoView = DestroyTetrimino;
                x.blockPool = mBlockPool;
            };

            mPlayfield = new Playfield(mGameSettings);
            mPlayfield.OnCurrentPieceReachBottom = CreateTetrimino;
            mPlayfield.OnGameOver = SetGameOver;
            mPlayfield.OnDestroyLine = DestroyLine;

            RestartGame();
        }

        //Called when the game starts and when user click Restart Game on GameOver screen
        //Responsable for restaring all necessary components
        public void RestartGame()
        {
            music.Play("game_start");
            LevelUp.instance.SetStage(0);
            GameOver.instance.HideScreen(0f);
            StartGame.instance.HideScreen(1f);

            Score.instance.ResetScore(stages);
            mTimer = 0f;

            mPlayfield.ResetGame();
            mTetriminoPool.ReleaseAll();
            mTetriminos.Clear();

            CreateTetrimino();
            mGameIsOver = false;
        }

        private void LoadSettings()
        {
            // Checks for the json file
            if (!System.IO.File.Exists(JSON_PATH))
                throw new System.Exception(string.Format("GameSettings.json could not be found inside {0}. Create one in Window>GameSettings Creator.", JSON_PATH));

            // Loads the GameSettings Json
            var json = System.IO.File.ReadAllText(JSON_PATH);
            mGameSettings = JsonUtility.FromJson<GameSettings>(json);
            mGameSettings.CheckValidSettings();
        }

        //Callback from Playfield to destroy a line in view
        private void DestroyLine(int y)
        {
            mTetriminos.ForEach(x => x.DestroyLine(y));
            mTetriminos.RemoveAll(x => x == null);

            Score.instance.AddPoints(mGameSettings.pointsByBreakingLine);

            int rowsCleared = Score.instance.PlayerScore / mGameSettings.pointsByBreakingLine;
            music.Play("clear_row");
            music.RTPC("score", Score.instance.PlayerScore);
            if (rowsCleared >= stages[stages.Length - 1])
            {
                SetGameOver(true);
                return;
            }
            for (int i = stages.Length - 2; i >= 0; i--)
            {
                if (rowsCleared >= stages[i])
                {
                    IncStage(i + 1);
                    break;
                }
            }
        }

        public void IncStage(int stage)
        {
            music.Play("stage_complete");
            currentLevel = stage;
            IncreaseSpeed();
            LevelUp.instance.SetStage(stage);
            Score.instance.SetStage(stage);
        }

        private void IncreaseSpeed()
        {

            if (timeToStep >= 0.6f)
            {
                timeToStep -= 0.2f;
                Score.instance.SetSpeed();
            }
            if (mGameSettings.debugMode)
                Debug.Log("current speed: " + timeToStep);
        }

        //Callback from Playfield to show game over in view
        private void SetGameOver(bool isWin = false)
        {
            mGameIsOver = true;

            if (isWin)
            {
                music.Play("round_win");
            }
            else
            {
                music.Play("round_lose");
            }

            GameOver.instance.ShowScreen(isWin, stages.Length);
        }

        //Call to the engine to create a new piece and create a representation of the random piece in view
        private void CreateTetrimino()
        {
            if (mCurrentTetrimino != null)
            {
                music.Play("land_shape");
                mCurrentTetrimino.isLocked = true;
            }

            var tetrimino = mPlayfield.CreateTetrimo();
            var tetriminoView = mTetriminoPool.Collect();
            tetriminoView.InitiateTetrimino(tetrimino);
            mTetriminos.Add(tetriminoView);

            if (mPreview != null)
                mTetriminoPool.Release(mPreview);

            mPreview = mTetriminoPool.Collect();
            mPreview.InitiateTetrimino(tetrimino, true);
            mRefreshPreview = true;
        }

        //When all the blocks of a piece is destroyed, we must release ("destroy") it.
        private void DestroyTetrimino(TetriminoView obj)
        {
            var index = mTetriminos.FindIndex(x => x == obj);
            mTetriminoPool.Release(obj);
            mTetriminos[index] = null;
        }

        //Regular Unity Update method
        //Responsable for counting down and calling Step
        //Also responsable for gathering users input
        public void Update()
        {
            if (mGameIsOver) return;

            mTimer += Time.deltaTime;

            if (mTimer > timeToStep)
            {
                mTimer = 0;
                mPlayfield.Step();
            }

            if (mCurrentTetrimino == null) return;
            int x = mCurrentTetrimino.currentPosition.x;
            int y = mCurrentTetrimino.currentPosition.y;

            // Rotates Right
            if (Input.GetKeyDown(mGameSettings.rotateRightKey))
            {
                Debug.Log(mTimer);
                music.Play("flip_shape");
                if (mPlayfield.IsPossibleMovement(x, y, mCurrentTetrimino, mCurrentTetrimino.NextRotation))
                {
                    mCurrentTetrimino.currentRotation = mCurrentTetrimino.NextRotation;
                    mRefreshPreview = true;
                }
            }

            // Rotates Left
            if (Input.GetKeyDown(mGameSettings.rotateLeftKey))
            {
                music.Play("flip_shape");
                if (mPlayfield.IsPossibleMovement(x, y, mCurrentTetrimino, mCurrentTetrimino.PreviousRotation))
                {
                    mCurrentTetrimino.currentRotation = mCurrentTetrimino.PreviousRotation;
                    mRefreshPreview = true;
                }
            }

            // Moves piece to the left
            if (Input.GetKeyDown(mGameSettings.moveLeftKey))
            {
                music.Play("move_shape_left");
                if (mPlayfield.IsPossibleMovement(x - 1, y, mCurrentTetrimino, mCurrentTetrimino.currentRotation))
                {
                    mCurrentTetrimino.currentPosition = new Vector2Int(x - 1, y);
                    mRefreshPreview = true;
                }
            }

            // Moves piece to the right
            if (Input.GetKeyDown(mGameSettings.moveRightKey))
            {
                music.Play("move_shape_right");
                if (mPlayfield.IsPossibleMovement(x + 1, y, mCurrentTetrimino, mCurrentTetrimino.currentRotation))
                {
                    mCurrentTetrimino.currentPosition = new Vector2Int(x + 1, y);
                    mRefreshPreview = true;
                }
            }

            // Accelerates fall. 
            // Using GetKey instead of GetKeyDown, because most of the time, users want to keep this button pressed and make the piece fall
            if (Input.GetKey(mGameSettings.moveDownKey))
            {
                if (mPlayfield.IsPossibleMovement(x, y + 1, mCurrentTetrimino, mCurrentTetrimino.currentRotation))
                {
                    mCurrentTetrimino.currentPosition = new Vector2Int(x, y + 1);
                }
            }

            // Renders preview tetris piece.
            if (mRefreshPreview)
            {
                int tempX = mCurrentTetrimino.currentPosition.x;
                int tempY = mCurrentTetrimino.currentPosition.y;
                while (mPlayfield.IsPossibleMovement(tempX, tempY, mCurrentTetrimino, mCurrentTetrimino.currentRotation))
                {
                    tempY++;
                }

                mPreview.ForcePosition(tempX, tempY - 1);
                mRefreshPreview = false;
            }
        }
    }
}
